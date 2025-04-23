<template>
    <div class="modal-overlay" @click.self="closeModal">
      <div class="modal-content file-preview-modal">
        <button @click="closeModal" class="close-button" title="Закрыть">×</button>
        <h3>Предпросмотр: {{ originalName }}</h3>
  
        <div v-if="isLoading" class="loading-indicator">Загрузка данных файла...</div>
        <div v-if="error" class="error-message">{{ error }}</div>
  
        <div ref="previewAreaRef" class="preview-area" v-show="!isLoading && !error">
          <div v-if="isRendering" class="rendering-loader">
              <div class="loading-indicator small">{{ renderingStatus }}</div>
          </div>
  
          <template v-if="!isRendering">
              <!-- Изображение -->
              <img v-if="isPreviewableImage && blobUrl" :src="blobUrl" :alt="originalName" class="preview-image"/>
              <!-- Видео -->
              <video v-else-if="isPreviewableVideo && blobUrl" :src="blobUrl" controls class="preview-video"></video>
              <!-- PDF -->
              <iframe v-else-if="isPreviewablePdf && blobUrl" :src="blobUrl" frameborder="0" class="preview-pdf" title="PDF Preview"></iframe>
              <!-- DOCX -->
              <div v-else-if="isPreviewableDocx" ref="docxContainerRef" class="docx-container"></div>
              <!-- PPTX (Текст) -->
               <div v-else-if="isPreviewablePptx && pptxTextContent" class="pptx-text-container" v-html="pptxTextContent"></div>
              <!-- CSV -->
               <div v-else-if="isPreviewableCsv && csvData.length > 0" class="csv-container">
                   <!-- ... таблица CSV ... -->
               </div>
              <!-- XLSX и другие неподдерживаемые -->
              <div v-else class="preview-not-available">
                <p>Предпросмотр недоступен для данного типа файла ({{ props.contentType || getExtension() || 'тип не определен' }}).</p>
                <button @click="downloadOriginal" class="download-button">Скачать файл</button>
              </div>
          </template>
        </div>
         <div v-if="!isLoading && !error && !blobUrl && !fileBlob && !pptxFileBlob && !csvText && pptxTextContent === ''" class="preview-not-available">
             <p>Не удалось загрузить данные для предпросмотра.</p>
             <button @click="downloadOriginal" class="download-button">Скачать файл</button>
         </div>
  
      </div>
    </div>
  </template>
  
  <script setup>
  import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue';
  import axios from 'axios';
  import { renderAsync } from 'docx-preview';
  import JSZip from 'jszip';
  import convert from 'xml-js';
  
  const props = defineProps({
    fileId: { type: String, required: true },
    contentType: { type: String, default: '' },
    originalName: { type: String, default: 'файл' },
  });
  const emit = defineEmits(['close', 'download-original']);
  
  const isLoading = ref(false); // Загрузка blob/text
  const isRendering = ref(false); // Рендеринг docx/pptx/csv
  const renderingStatus = ref(''); // Текст для лоадера рендеринга
  const error = ref('');
  const blobUrl = ref(null); // Для PDF, Image, Video
  const fileBlob = ref(null); // Для docx
  const pptxFileBlob = ref(null); // Отдельно для pptx, чтобы не конфликтовать
  const pptxTextContent = ref(''); // Для извлеченного текста PPTX
  const csvText = ref(''); // Для CSV
  const csvData = ref([]); // Распарсенные данные CSV
  
  const previewAreaRef = ref(null);
  const docxContainerRef = ref(null);
  
  const getMimeType = () => props.contentType?.split(';')[0].trim().toLowerCase() || '';
  const getExtension = () => props.originalName?.split('.').pop()?.toLowerCase() || '';
  
  const isPreviewableImage = computed(() => getMimeType().startsWith('image/'));
  const isPreviewableVideo = computed(() => getMimeType().startsWith('video/'));
  const isPreviewablePdf = computed(() => ['application/pdf', 'application/x-pdf'].includes(getMimeType()));
  const isPreviewableDocx = computed(() => getMimeType() === 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' || getExtension() === 'docx');
  const isPreviewablePptx = computed(() => ['application/vnd.openxmlformats-officedocument.presentationml.presentation','application/vnd.ms-powerpoint'].includes(getMimeType()) || ['pptx', 'ppt'].includes(getExtension()));
  const isPreviewableCsv = computed(() => getMimeType() === 'text/csv' || (getMimeType() === 'text/plain' && getExtension() === 'csv'));
  
  const closeModal = () => { emit('close'); };
  const downloadOriginal = () => { emit('download-original', props.fileId); closeModal(); };
  
  const revokeUrls = () => {
    if (blobUrl.value) { URL.revokeObjectURL(blobUrl.value); blobUrl.value = null; }
    fileBlob.value = null;
    pptxFileBlob.value = null;
    csvText.value = '';
    csvData.value = [];
    pptxTextContent.value = '';
  };
  
  const renderDocx = async () => {
  if (!fileBlob.value || !docxContainerRef.value) return;
  isRendering.value = true; renderingStatus.value = 'Рендеринг DOCX...'; error.value = '';
  //console.log('Attempting to render DOCX...');
  try {
    await renderAsync(fileBlob.value, docxContainerRef.value, null, {
      className: "docx",
      breakPages: true,
      experimental: true,
      debug: process.env.NODE_ENV !== 'production',
      useMathMLPolyfill: true,
      inWrapper: true,
      ignoreWidth: false,
      ignoreHeight: false,
      ignoreFonts: false
    });
    //console.log('DOCX rendered successfully.');
  } catch (renderError) {
    //console.error("Error rendering DOCX:", renderError);
    error.value = `Ошибка отображения DOCX. Попробуйте скачать файл.`;
    if(docxContainerRef.value) { docxContainerRef.value.innerHTML = ''; }
  } finally {
    isRendering.value = false; renderingStatus.value = '';
  }
};
  
  const extractPptxText = async () => {
      if (!pptxFileBlob.value) return;
      isRendering.value = true; renderingStatus.value = 'Извлечение текста PPTX...'; error.value = '';
      //console.log('Attempting to extract PPTX text...');
      let extractedHtml = '';
      try {
          const zip = await JSZip.loadAsync(pptxFileBlob.value);
          const slidePromises = [];
  
          zip.folder("ppt/slides").forEach((relativePath, file) => {
               if (/^slide\d+\.xml$/i.test(relativePath)) {
                   console.log('Processing slide:', relativePath);
                   slidePromises.push(file.async("string"));
               }
          });
  
          if (slidePromises.length === 0) {
               //console.warn("No slide*.xml files found in ppt/slides folder.");
               error.value = "Не удалось найти слайды в файле PPTX.";
               isRendering.value = false; renderingStatus.value = '';
               return;
          }
  
  
          const slideXmls = await Promise.all(slidePromises);
          let slideCounter = 0;
  
          slideXmls.forEach(xmlString => {
              slideCounter++;
              extractedHtml += `<h4>Слайд ${slideCounter}</h4>`;
               try {
                   const result = convert.xml2js(xmlString, { compact: true, spaces: 2 });
                   const textNodes = findNodes(result, 'a:t');
  
                   if (textNodes.length > 0) {
                      extractedHtml += '<ul>';
                      textNodes.forEach(node => {
                          const text = node && (node._text || (node['a:r'] && node['a:r']._text));
                          if (text) {
                              extractedHtml += `<li>${escapeHtml(text)}</li>`;
                          }
                      });
                      extractedHtml += '</ul>';
                   } else {
                       extractedHtml += '<p><i>(Нет текста на слайде)</i></p>';
                   }
               } catch (xmlError) {
                   //console.error(`Error parsing XML for slide ${slideCounter}:`, xmlError);
                   extractedHtml += `<p style="color: red;"><i>(Ошибка обработки слайда ${slideCounter})</i></p>`;
               }
               extractedHtml += '<hr/>';
          });
  
           if (!extractedHtml) {
               error.value = "Не удалось извлечь текст из презентации.";
           } else {
              pptxTextContent.value = extractedHtml;
              //console.log('PPTX text extracted successfully.');
           }
  
      } catch (zipError) {
          //console.error("Error processing PPTX file:", zipError);
          error.value = "Ошибка обработки файла PPTX. Возможно, файл поврежден или имеет неверный формат.";
      } finally {
          isRendering.value = false; renderingStatus.value = '';
      }
  };
  
  const findNodes = (obj, nodeName) => {
      let nodes = [];
      if (!obj || typeof obj !== 'object') return nodes;
  
      if (obj._attributes && obj._attributes['name'] === nodeName) { // finally
      }
       if (obj[nodeName]) {
           const found = obj[nodeName];
           nodes = nodes.concat(Array.isArray(found) ? found : [found]);
       }
  
  
      for (const key in obj) {
          if (key !== '_attributes' && key !== '_text' && key !== '_cdata' && key !== '_comment') {
               if (typeof obj[key] === 'object') {
                   nodes = nodes.concat(findNodes(obj[key], nodeName));
               }
          }
      }
      return nodes;
  };
  
  const escapeHtml = (unsafe) => {
      if (typeof unsafe !== 'string') return '';
      return unsafe
           .replace(/&/g, "&")
           .replace(/</g, "<")
           .replace(/>/g, ">")
           .replace(/"/g, "\"")
           .replace(/'/g, "'")
   }
  
  
  const parseAndDisplayCsv = () => {
      if (!csvText.value) return;
      isRendering.value = true; renderingStatus.value = 'Обработка CSV...'; error.value = '';
      //console.log('Parsing CSV data...');
      try {
          const lines = csvText.value.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
          const data = lines
              .map(line => line.trim())
              .filter(line => line.length > 0)
               .map(line => {
                  const result = [];
                  let current = '';
                  let inQuotes = false;
                  for (let i = 0; i < line.length; i++) {
                      const char = line[i];
                      if (char === '"' && (i === 0 || line[i-1] !== '"')) { // Одиночная кавычка
                          inQuotes = !inQuotes;
                      } else if (char === ',' && !inQuotes) {
                          result.push(current.trim().replace(/^"(.*)"$/, '$1').replace(/""/g, '"'));
                          current = '';
                      } else if (char === '"' && line[i+1] === '"') { // Двойная кавычка внутри кавычек
                          current += '"';
                          i++;
                      } else {
                          current += char;
                      }
                  }
                  result.push(current.trim().replace(/^"(.*)"$/, '$1').replace(/""/g, '"')); // Добавляем последнее поле
                  return result;
              });
  
  
          if (data.length === 0) {
              //console.warn("CSV parsing resulted in empty data array.");
              error.value = "Не удалось распознать данные в CSV файле.";
              csvData.value = [];
          } else {
              //console.log(`CSV parsed into ${data.length} rows.`);
              csvData.value = data;
          }
      } catch (parseError) {
          //console.error("Error parsing CSV:", parseError);
          error.value = "Ошибка обработки CSV файла.";
          csvData.value = [];
      } finally {
          isRendering.value = false; renderingStatus.value = '';
      }
  };
  
  
  const fetchPreview = async () => {
    if (!props.fileId) return;
    isLoading.value = true; error.value = ''; revokeUrls(); isRendering.value = false;
    //console.log(`FilePreviewModal: Fetching preview for File ID: ${props.fileId}, Type: ${props.contentType}, Name: ${props.originalName}`);

    const responseType = isPreviewableCsv.value ? 'text' : 'blob';
    //console.log(`Requesting with responseType: ${responseType}`);

    try {
        const response = await axios.get(`/api/file/download/${props.fileId}`, {
        responseType: responseType, params: { inline: true }
        });
        //console.log('>>> Preview Data Received:', response.data);

        if (responseType === 'text') {
            if (typeof response.data === 'string' && response.data.length > 0) { csvText.value = response.data; }
            else { console.warn("Empty text response for CSV"); error.value = "CSV файл пуст."; }
        } else {
            if (response.data instanceof Blob && response.data.size > 0) {
                if (isPreviewableDocx.value) { fileBlob.value = response.data; }
                else if (isPreviewablePptx.value) { pptxFileBlob.value = response.data; }
                else { blobUrl.value = URL.createObjectURL(response.data); console.log('>>> Created Blob URL:', blobUrl.value); }
            } else { console.warn("Empty/invalid blob received"); error.value = "Получены пустые данные."; }
        }
    } catch (err) {
      //console.error(`Error fetching file for preview (File ID: ${props.fileId}):`, err);
      if (err.response) {
           //console.error("Preview Error Response Status:", err.response.status);
           //console.error("Preview Error Response Data:", err.response.data);
           if (err.response.status === 404) error.value = "Файл не найден.";
           else if (err.response.status === 403) error.value = "Доступ к файлу запрещен.";
           else error.value = `Ошибка загрузки предпросмотра (статус ${err.response.status}).`;
      } else if (err.request) {
           error.value = "Нет ответа от сервера при загрузке предпросмотра.";
      }
      else {
          error.value = "Не удалось загрузить файл для предпросмотра.";
      }
    } finally {
      isLoading.value = false;
      // Запускаем рендеринг/парсинг после обновления DOM
      await nextTick();
      if (isPreviewableDocx.value && fileBlob.value) { renderDocx(); }
      else if (isPreviewablePptx.value && pptxFileBlob.value) { extractPptxText(); }
      else if (isPreviewableCsv.value && csvText.value) { parseAndDisplayCsv(); }
    }
  };
  
  onMounted(fetchPreview);
  onBeforeUnmount(revokeUrls);
  watch(() => props.fileId, (newFileId, oldFileId) => { if (newFileId !== oldFileId) { fetchPreview(); } });
  
  </script>
  
  <style> /* Глобальные стили для DOCX и PPTX */
  .docx-wrapper { background-color: #eef1f5; padding: 20px; }
  .docx-wrapper .docx { background-color: white; box-shadow: 0 1px 5px rgba(0,0,0,0.2); margin: 0 auto 15px auto; /* Центрируем страницу */ width: 21cm; min-height: 29.7cm; padding: 1.5cm; box-sizing: border-box; }
  .docx-wrapper .docx p { margin-bottom: 0.5em; line-height: 1.4; }
  .docx-wrapper .docx table { border-collapse: collapse; width: 100%; margin-bottom: 1em; }
  .docx-wrapper .docx td, .docx-wrapper .docx th { border: 1px solid #ccc; padding: 5px 8px; text-align: left; }
  
  </style>
  
  <style scoped> /* Стили компонента */
      .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background-color: rgba(0, 0, 0, 0.7); display: flex; justify-content: center; align-items: center; z-index: 1000; padding: 20px; box-sizing: border-box; }
      .modal-content { background-color: white; padding: 25px; border-radius: 8px; max-width: 90vw; max-height: 90vh; overflow: hidden; position: relative; box-shadow: 0 5px 15px rgba(0,0,0,0.3); display: flex; flex-direction: column; }
      .file-preview-modal { min-width: 70vw; min-height: 85vh; }
      .close-button { position: absolute; top: 10px; right: 15px; background: none; border: none; font-size: 1.8rem; cursor: pointer; color: #888; padding: 0; line-height: 1; z-index: 10; }
      .close-button:hover { color: #333; }
      h3 { margin-top: 0; margin-bottom: 15px; color: #333; text-align: center; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; padding-right: 30px; }
      .loading-indicator { text-align: center; padding: 30px; color: #6c757d; font-size: 1.1em; flex-grow: 1; display: flex; justify-content: center; align-items: center;}
      .error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; padding: 15px; text-align: center; margin: 20px 0;}
  
      .preview-area {
          margin-top: 10px;
          flex-grow: 1;
          display: flex;
          justify-content: center;
          align-items: flex-start;
          background-color: #eef1f5;
          border-radius: 4px;
          overflow: auto; /* Скролл для всей области */
          padding: 0;
          position: relative;
      }
      .rendering-loader { /* Лоадер рендеринга */
           position: absolute; top: 0; left: 0; width: 100%; height: 100%;
           background-color: rgba(238, 241, 245, 0.9);
           display: flex; justify-content: center; align-items: center;
           z-index: 5;
       }
       .rendering-loader .loading-indicator.small { font-size: 1em; padding: 10px; background-color: white; border-radius: 4px; box-shadow: 0 1px 3px rgba(0,0,0,0.2); }
  
      .preview-image { max-width: 100%; max-height: none; object-fit: contain; display: block; margin: auto; padding: 10px; box-sizing: border-box;}
      .preview-video { width: 100%; height: 100%; max-height: 100%; display: block; }
      .preview-pdf { width: 100%; height: 100%; border: none; min-height: 75vh; }
  
      .docx-container, .pptx-text-container, .csv-container {
          width: 100%;
          height: 100%;
          padding: 15px;
          box-sizing: border-box;
          background-color: white;
          overflow: visible;
          text-align: left; /* Выравнивание по левому краю для текста */
      }
       /* Стили для извлеченного текста PPTX */
      .pptx-text-container h4 { margin-top: 0; margin-bottom: 10px; padding-bottom: 5px; border-bottom: 1px solid #eee; font-size: 1.1em; color: #333; }
      .pptx-text-container ul { padding-left: 20px; margin-top: 5px; margin-bottom: 15px; list-style: disc; }
      .pptx-text-container li { margin-bottom: 5px; line-height: 1.4; }
      .pptx-text-container hr { border: none; border-top: 1px dashed #ccc; margin: 20px 0; }
      .pptx-text-container p { font-style: italic; color: #666; margin: 10px 0;}
  
       /* Стили для CSV таблицы */
      .csv-container table { width: 100%; border-collapse: collapse; font-size: 0.9em; }
      .csv-container th, .csv-container td { border: 1px solid #ccc; padding: 6px 8px; text-align: left; background-color: white; white-space: pre-wrap; /* Перенос текста внутри ячеек */}
      .csv-container th { background-color: #f8f9fa; font-weight: bold; position: sticky; top: -1px; /* Фиксируем заголовки при скролле (-1px чтобы рамка была видна) */ z-index: 1; }
  
  
      .preview-not-available { text-align: center; padding: 40px 20px; color: #555; align-self: center; max-width: 400px;}
      .download-button { margin-top: 15px; padding: 10px 18px; background-color: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; transition: background-color 0.2s; }
      .download-button:hover { background-color: #0056b3; }
  </style>