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

          <img v-if="isPreviewableImage && blobUrl" :src="blobUrl" :alt="originalName" class="preview-image"/>
          <video v-else-if="isPreviewableVideo && blobUrl" :src="blobUrl" controls class="preview-video"></video>
          <iframe v-else-if="isPreviewablePdf && blobUrl" :src="blobUrl" frameborder="0" class="preview-pdf" title="PDF Preview"></iframe>

          <div v-else-if="isPreviewableDocx" ref="docxContainerRef" class="docx-container">
          </div>

          <div v-else-if="isPreviewablePptx && pptxTextContent" class="pptx-text-container" v-html="pptxTextContent"></div>

          <div v-else-if="isPreviewableCsv && csvData.length > 0" class="csv-container">
              <div v-if="csvData.length > 0" class="csv-table-wrapper">
                  <table>
                      <thead>
                          <tr>
                              <th v-for="(header, hIndex) in csvData[0]" :key="hIndex">{{ header }}</th>
                          </tr>
                      </thead>
                      <tbody>
                          <tr v-for="(row, rIndex) in csvData.slice(1)" :key="rIndex">
                              <td v-for="(cell, cIndex) in row" :key="cIndex">{{ cell }}</td>
                          </tr>
                      </tbody>
                  </table>
              </div>
          </div>

          <!-- Неподдерживаемый тип -->
          <div v-else class="preview-not-available">
              <p>Предпросмотр недоступен для данного типа файла ({{ props.contentType || getExtension() || 'тип не определен' }}).</p>
              <button @click="downloadOriginal" class="download-button">Скачать файл</button>
          </div>

        </div>


        <div v-if="!isLoading && !error && !blobUrl && !fileBlob && !pptxFileBlob && !csvText && pptxTextContent === '' && csvData.length === 0" class="preview-not-available">
            <p>Не удалось загрузить данные для предпросмотра или файл пуст/поврежден.</p>
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
  //console.log('Rendering into:', docxContainerRef.value);
  //console.log('Using Blob:', fileBlob.value);
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
  console.error("Error rendering DOCX:", renderError);
   if (!docxContainerRef.value || docxContainerRef.value.children.length === 0) {
        error.value = `Ошибка отображения DOCX. Попробуйте скачать файл. (${renderError.message || renderError})`;
        if(docxContainerRef.value) { docxContainerRef.value.innerHTML = ''; } // Очищаем, если ошибка критическая
   } else {
        console.warn("DOCX rendering finished with errors, but some content might be visible.", renderError);
        // Не показываем ошибку пользователю, если что-то отрендерилось
        error.value = '';
   }
  } finally {
    isRendering.value = false;
    renderingStatus.value = '';
  }
};



const findNodes = (obj, nodeName) => {
    let nodes = [];
    if (!obj || typeof obj !== 'object') return nodes;

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
                    if (char === '"' && (i === 0 || line[i-1] !== '"')) { 
                        inQuotes = !inQuotes;
                    } else if (char === ',' && !inQuotes) {
                        result.push(current.trim().replace(/^"(.*)"$/, '$1').replace(/""/g, '"'));
                        current = '';
                    } else if (char === '"' && line[i+1] === '"') { 
                        current += '"';
                        i++;
                    } else {
                        current += char;
                    }
                }
                result.push(current.trim().replace(/^"(.*)"$/, '$1').replace(/""/g, '"'));
                return result;
            });


        if (data.length === 0 || (data.length === 1 && data[0].join('').trim() === '')) {
            //console.warn("CSV parsing resulted in empty data array.");
            error.value = "Не удалось распознать данные в CSV файле или файл пуст.";
            csvData.value = [];
        } else {
            //console.log(`CSV parsed into ${data.length} rows.`, data);
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

const extractPptxText = async () => {
    if (!pptxFileBlob.value) return;
    isRendering.value = true; renderingStatus.value = 'Извлечение текста PPTX...'; error.value = '';
    console.log('Attempting to extract PPTX text...');
    let extractedHtml = '';
    try {
        const zip = await JSZip.loadAsync(pptxFileBlob.value);
        const slidePromises = [];

        zip.folder("ppt/slides").forEach((relativePath, file) => {
             if (/^slide\d+\.xml$/i.test(relativePath)) {
                 //console.log('Processing slide:', relativePath);
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
                        const text = node && node._text ? node._text : '';
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

         if (!extractedHtml.replace(/<h4>Слайд \d+<\/h4><p><i>\(Нет текста на слайде\)<\/i><\/p><hr\/>/g, '').trim()) {
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


const fetchPreview = async () => {
  if (!props.fileId) return;
  isLoading.value = true; error.value = ''; revokeUrls(); isRendering.value = false;
  console.log(`FilePreviewModal: Fetching preview for File ID: ${props.fileId}, Type: ${props.contentType}, Name: ${props.originalName}`); 

  const responseType = isPreviewableCsv.value || isPreviewablePptx.value ? 'blob' : 'blob'; 
  console.log(`Requesting with responseType: ${responseType}`); 

  try {
      const response = await axios.get(`/api/file/download/${props.fileId}`, {
      responseType: responseType, params: { inline: true }
      });
      console.log('>>> Preview Data Received:', response.data); 

      if (isPreviewableCsv.value && responseType === 'text') {
          if (typeof response.data === 'string' && response.data.length > 0) { csvText.value = response.data; }
          else { console.warn("Empty text response for CSV"); error.value = "CSV файл пуст."; }
      } else if (response.data instanceof Blob && response.data.size > 0) {
          if (isPreviewableDocx.value) { fileBlob.value = response.data; }
          else if (isPreviewablePptx.value) { pptxFileBlob.value = response.data; }
          else if (isPreviewableCsv.value) { // Если CSV пришел как Blob, читаем его как текст
              const reader = new FileReader();
              reader.onload = (e) => {
                  if (e.target.result && typeof e.target.result === 'string') {
                      csvText.value = e.target.result;
                      parseAndDisplayCsv(); 
                  } else {
                      //console.warn("Failed to read CSV blob as text");
                      error.value = "Не удалось прочитать данные CSV файла.";
                  }
              };
              reader.onerror = (e) => {
                  //console.error("FileReader error for CSV blob:", e);
                  error.value = "Ошибка чтения данных CSV файла.";
              };
              reader.readAsText(response.data);
          }
          else { blobUrl.value = URL.createObjectURL(response.data); console.log('>>> Created Blob URL:', blobUrl.value); }
      } else { console.warn("Empty/invalid blob received"); error.value = "Получены пустые данные."; }

  } catch (err) {
    //console.error(`Error fetching file for preview (File ID: ${props.fileId}):`, err);
    if (err.response) {
         //console.error("Preview Error Response Status:", err.response.status);
         //console.error("Preview Error Response Data:", err.response.data);
         if (err.response.status === 404) error.value = "Файл не найден.";
         else if (err.response.status === 403) error.value = "Доступ к файлу запрещен.";
         else if (err.response.status === 401) error.value = "Сессия истекла или доступ неавторизован.";
         else error.value = `Ошибка загрузки предпросмотра (статус ${err.response.status}).`;
    } else if (err.request) {
         error.value = "Нет ответа от сервера при загрузке предпросмотра.";
    }
    else {
        error.value = "Не удалось загрузить файл для предпросмотра.";
    }
  } finally {
    isLoading.value = false;
    await nextTick();
    if (isPreviewableDocx.value && fileBlob.value) { renderDocx(); }
    else if (isPreviewablePptx.value && pptxFileBlob.value) { extractPptxText(); }
  }
};


onMounted(fetchPreview);
onBeforeUnmount(revokeUrls);
watch(() => props.fileId, (newFileId, oldFileId) => { if (newFileId !== oldFileId) { fetchPreview(); } });

</script>

<style>
.docx-wrapper {
    background-color: #eef1f5; /* Светло-серый фон вокруг страницы */
    padding: 20px; /* Отступы вокруг страницы */
    display: flex;
    flex-flow: column;
    align-items: center; /* Центрирование страницы по горизонтали */
    box-sizing: border-box; /* Учитываем padding в размерах */
    min-height: 100%; /* Занимаем всю доступную высоту родителя preview-area */
    width: 100%; /* Занимаем всю доступную ширину родителя preview-area */
}

/* Стили для самой "страницы" внутри docx-wrapper */
.docx-wrapper > section.docx {
    background-color: white; /* Белый фон самой страницы */
    box-shadow: 0 1px 5px rgba(0,0,0,0.2); /* Тень вокруг страницы */
    margin: 0 auto 15px auto; /* Центрирование страницы и отступ снизу */
    width: 21cm; /* Стандартная ширина A4 */
    min-height: 29.7cm; /* Стандартная высота A4 */
    padding: 1.5cm; /* Поля страницы */
    box-sizing: border-box; /* Учитываем padding в размерах */
    overflow: visible; /* Позволяем контенту выходить, если нужно (например, изображения) */
}

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
          flex-grow: 1; /* Занимает все доступное место по вертикали */
          display: flex; /* Используем Flexbox для управления содержимым */
          flex-direction: column; /* Элементы внутри располагаются вертикально */
          align-items: center; /* Центрируем содержимое по горизонтали, если оно уже preview-area */
          /* justify-content: flex-start; Оставим дефолтное, align-items: flex-start на flex-direction: column */
          /* background-color: rgba(255, 165, 0, 0.3); /* Удалить отладочный фон */
          border-radius: 4px;
          overflow: auto; /* ГЛАВНЫЙ СКРОЛЛ для области предпросмотра */
          padding: 0; /* Убрать отступы, чтобы docx-wrapper мог управлять своими полями */
          position: relative;
          /* border: 5px dashed orange; /* Удалить отладочную рамку */
          min-height: 300px; /* Сохранить минимальную высоту */
          width: 100%; /* Убедиться, что занимает всю ширину */
          box-sizing: border-box;
          /* Добавим возможность сжиматься, если контент очень большой */
          min-width: 0;
          min-height: 0;
      }
    .rendering-loader { /* Лоадер рендеринга */
         position: absolute; top: 0; left: 0; width: 100%; height: 100%;
         background-color: rgba(238, 241, 245, 0.9);
         display: flex; justify-content: center; align-items: center;
         z-index: 5;
     }
     .rendering-loader .loading-indicator.small { font-size: 1em; padding: 10px; background-color: white; border-radius: 4px; box-shadow: 0 1px 3px rgba(0,0,0,0.2); }

    .preview-image { max-width: 100%; max-height: none; object-fit: contain; display: block; margin: auto; padding: 10px; box-sizing: border-box;}
    .preview-video { width: 100%; height: auto; max-height: 100%; display: block; margin: auto;} /* Изменено height на auto, max-height: 100% */
    .preview-pdf { width: 100%; height: 100%; border: none; min-height: 75vh; display: block;} /* Добавлен display: block */

    .docx-container {
      width: 100%;
      height: auto;

      box-sizing: border-box;

    }
      .pptx-text-container, .csv-container {
        width: 100%;
        height: auto; /* Высота по контенту */
        padding: 15px;
        box-sizing: border-box;
        background-color: white; /* Или другой фон для этих блоков */
        overflow: visible; /* Позволяет контенту выходить, если нужно */
        text-align: left; /* Выравнивание по левому краю для текста */
        margin-bottom: 15px;
    }
     .csv-table-wrapper {
         width: 100%;
         overflow-x: auto; /* Добавляем горизонтальный скролл для таблицы */
     }
     .csv-container table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.9em;
        table-layout: auto; /* Позволяет ширине колонок определяться контентом */
     }
     .csv-container th, .csv-container td {
         border: 1px solid #ccc;
         padding: 6px 8px;
         text-align: left;
         background-color: white;
         white-space: pre-wrap; /* Перенос текста внутри ячеек */
     }
     .csv-container th {
         background-color: #f8f9fa;
         font-weight: bold;
         position: sticky;
         top: 0; /* Фиксируем заголовки при вертикальном скролле */
         z-index: 1;
     }


    .preview-not-available { text-align: center; padding: 40px 20px; color: #555; align-self: center; max-width: 400px;}
    .download-button { margin-top: 15px; padding: 10px 18px; background-color: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; transition: background-color 0.2s; }
    .download-button:hover { background-color: #0056b3; }
</style>