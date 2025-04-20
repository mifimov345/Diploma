<template>
  <div class="file-upload-container">
    <h2>Загрузка файлов</h2>

    <!-- Зона Drag & Drop -->
    <div
      class="drop-zone"
      :class="{ 'drag-over': isDragging }"
      @dragover.prevent="isDragging = true"
      @dragenter.prevent="isDragging = true"
      @dragleave.prevent="isDragging = false"
      @drop.prevent="handleDrop"
      role="button"
      tabindex="0"
      @click="triggerFileInput"
      @keypress.enter="triggerFileInput"
      @keypress.space="triggerFileInput"
    >
      <p>Перетащите файлы сюда или</p>
      <span class="file-input-label">Выберите файлы</span>
      <input
        ref="fileInputRef"
        id="file-input-hidden"
        type="file"
        multiple
        @change="handleFileSelect"
        accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,image/*,video/*"
        style="display: none"
      />
       <p class="drop-zone-hint">Поддерживаются документы, изображения, видео. Макс. размер: 100MB.</p>
    </div>

    <!-- Список загружаемых файлов и их статус -->
    <div v-if="uploadQueue.length > 0" class="upload-queue">
      <h4>Очередь загрузки:</h4>
      <ul>
        <!-- Убрали неиспользуемый index -->
        <li v-for="item in uploadQueue" :key="item.id" :class="['upload-item', item.status]">
          <span class="upload-filename" :title="item.file.name">{{ item.file.name }} ({{ formatBytes(item.file.size) }})</span>
          <span class="upload-status">
            <template v-if="item.status === 'pending'">Ожидание...</template>
            <template v-else-if="item.status === 'uploading'">Загрузка {{ item.progress }}%...</template>
            <template v-else-if="item.status === 'success'">✅ Успешно</template>
            <template v-else-if="item.status === 'error'">❌ Ошибка: {{ item.errorMsg }}</template>
            <template v-else-if="item.status === 'cancelled'">Отменено</template>
          </span>
           <!-- Индикатор прогресса -->
           <div v-if="item.status === 'uploading'" class="progress-bar-container">
                <div class="progress-bar" :style="{ width: item.progress + '%' }"></div>
           </div>
            <!-- Кнопка отмены для ожидающих или загружающихся -->
           <button
             v-if="item.status === 'pending' || item.status === 'uploading'"
             @click="cancelUpload(item.id)"
             class="cancel-item-button"
             title="Отменить загрузку"
             aria-label="Отменить загрузку файла">×</button>
        </li>
      </ul>
       <button @click="clearQueue" :disabled="isUploading" class="clear-queue-button">Очистить завершенные</button>
    </div>

    <!-- Общее сообщение -->
     <div v-if="overallMessage" :class="['message', overallMessageType]" role="status">
        {{ overallMessage }}
     </div>

  </div>
</template>

<script setup>
import { ref, shallowRef, nextTick } from 'vue'; // <-- Добавили nextTick
import axios, { CancelToken } from 'axios'; // Импортируем CancelToken
import { formatBytes } from '@/utils/formatters'; // Импортируем форматтер

const MAX_FILE_SIZE = 100 * 1024 * 1024; // 100MB
const ALLOWED_EXTENSIONS = ['.pdf','.doc','.docx','.xls','.xlsx','.ppt','.pptx','.txt','.csv']; // Документы
const ALLOWED_MIME_PREFIXES = ['image/', 'video/', 'text/']; // Изображения, видео, текст

const isDragging = ref(false);
// Используем shallowRef для массива с объектами File, т.к. сам File менять не нужно
const uploadQueue = shallowRef([]); // Массив объектов { id: number, file: File, status: string, progress: number, errorMsg: string, cancelTokenSource?: CancelTokenSource }
const overallMessage = ref('');
const overallMessageType = ref('success');
const isUploading = ref(false); // Флаг, что идет активный процесс загрузки
const fileInputRef = ref(null); // Ссылка на скрытый input
let nextItemId = 0; // Счетчик для уникальных ID элементов очереди

// --- Обработка выбора/дропа файлов ---
const triggerFileInput = () => { fileInputRef.value?.click(); };
const handleFileSelect = (event) => { handleFiles(event.target.files); if (event.target) event.target.value = null; };
const handleDrop = (event) => { isDragging.value = false; handleFiles(event.dataTransfer.files); };

const handleFiles = (fileList) => {
    if (!fileList || fileList.length === 0) return;
    overallMessage.value = '';
    let addedItems = []; // Собираем новые элементы

    for (let i = 0; i < fileList.length; i++) {
        const file = fileList[i];
        const fileId = nextItemId++;
        let errorMsg = '';

        // Проверка на дубликат в ТЕКУЩЕЙ очереди
        if (uploadQueue.value.some(item => item.file.name === file.name && item.file.size === file.size && item.status !== 'error' && item.status !== 'cancelled')) {
            console.warn(`File skipped (duplicate): ${file.name}`);
            continue;
        }

        // Проверка размера
        if (file.size > MAX_FILE_SIZE) {
            errorMsg = `Размер > ${MAX_FILE_SIZE / 1024 / 1024}MB`;
        }
        // Проверка типа
        else {
            const extension = `.${file.name?.split('.').pop()?.toLowerCase() || ''}`;
            const mimePrefix = file.type?.split('/')[0]?.toLowerCase() + '/' || '';
            if (!ALLOWED_EXTENSIONS.includes(extension) && !ALLOWED_MIME_PREFIXES.some(prefix => mimePrefix.startsWith(prefix))) {
                 errorMsg = 'Неподдерживаемый тип файла';
            }
        }

        addedItems.push({
             id: fileId,
             file: file,
             status: errorMsg ? 'error' : 'pending',
             progress: 0,
             errorMsg: errorMsg,
             cancelTokenSource: null
         });
    }

    // Добавляем новые элементы в начало очереди для наглядности
    if (addedItems.length > 0) {
         uploadQueue.value = [...addedItems, ...uploadQueue.value];
         // Запускаем обработку, если она не идет
         if (!isUploading.value) {
             processQueue();
         }
    } else if (uploadQueue.value.some(item => item.status === 'error')) {
         // Если ничего не добавили, но есть ошибки (например, только файлы с ошибками перетащили)
         overallMessage.value = 'Некоторые файлы не соответствуют требованиям.';
         overallMessageType.value = 'error';
    }
};

// --- Обработка очереди загрузки ---
const processQueue = async () => {
    // Находим первый ожидающий файл
    const pendingItemIndex = uploadQueue.value.findIndex(item => item.status === 'pending');

    // Если ожидающих нет или уже идет загрузка другого файла - выходим
    if (pendingItemIndex === -1 || isUploading.value) {
        // Если не идет загрузка и нет ожидающих - обновляем финальное сообщение
        if (!isUploading.value && pendingItemIndex === -1) {
             updateOverallMessage();
        }
        return;
    }

    isUploading.value = true; // Ставим флаг НАЧАЛА загрузки
    const currentItem = uploadQueue.value[pendingItemIndex];

    currentItem.status = 'uploading';
    currentItem.progress = 0;
    currentItem.errorMsg = '';
    currentItem.cancelTokenSource = CancelToken.source();

    const formData = new FormData();
    formData.append("file", currentItem.file);

    try {
        const apiUrl = '/api/file/upload';
        console.log(`FileUpload: Sending POST to ${axios.defaults.baseURL}${apiUrl} for ${currentItem.file.name}`);
        const response = await axios.post(apiUrl, formData, {
            headers: { 'Content-Type': 'multipart/form-data' },
            cancelToken: currentItem.cancelTokenSource.token,
            onUploadProgress: (progressEvent) => {
                 if (currentItem.status === 'uploading') { // Проверяем, не отменен ли
                      const percentCompleted = progressEvent.total
                        ? Math.round((progressEvent.loaded * 100) / progressEvent.total)
                        : 50; // Fallback для неопределенного total
                      currentItem.progress = Math.min(percentCompleted, 100);
                 }
            }
        });
         // Проверяем статус снова, т.к. мог быть отменен ПОСЛЕ запроса, но ДО ответа
         if (currentItem.status === 'uploading') {
             currentItem.status = 'success';
             currentItem.progress = 100;
             console.log(`File ${currentItem.file.name} uploaded successfully:`, response.data);
         }

    } catch (err) {
        if (axios.isCancel(err)) {
            currentItem.status = 'cancelled';
            console.log(`Upload cancelled for ${currentItem.file.name}`);
        } else {
            console.error(`Error uploading file ${currentItem.file.name}:`, err);
            // Устанавливаем ошибку только если статус не 'cancelled'
            if (currentItem.status !== 'cancelled') {
                currentItem.status = 'error';
                 if (err.response) { currentItem.errorMsg = err.response.data?.message || err.response.data || `Ошибка ${err.response.status}`; }
                 else if (err.request) { currentItem.errorMsg = 'Нет ответа'; }
                 else { currentItem.errorMsg = 'Ошибка'; }
            }
        }
    } finally {
         currentItem.cancelTokenSource = null; // Очищаем токен
         isUploading.value = false; // Снимаем флаг ЗАВЕРШЕНИЯ этой загрузки
         // Запускаем обработку следующего файла после обновления DOM
         await nextTick(); // Используем импортированный nextTick
         processQueue();
    }
};

const cancelUpload = (itemId) => {
    const item = uploadQueue.value.find(item => item.id === itemId);
    if (item) {
        if (item.status === 'uploading' && item.cancelTokenSource) {
             item.cancelTokenSource.cancel('Upload cancelled by user.');
             item.status = 'cancelled'; // Явно меняем статус
        } else if (item.status === 'pending') {
             item.status = 'cancelled'; // Просто меняем статус для ожидающих
        }
    }
};


const clearQueue = () => {
    // Удаляем только успешно загруженные, отмененные и с ошибками
    uploadQueue.value = uploadQueue.value.filter(item => item.status === 'pending' || item.status === 'uploading');
    overallMessage.value = '';
    // Если после очистки не осталось ожидающих/загружающихся, останавливаем флаг
    if (!uploadQueue.value.some(item => item.status === 'pending' || item.status === 'uploading')) {
        isUploading.value = false;
    }
};

const updateOverallMessage = () => {
    // Сообщение формируется только когда очередь полностью обработана (нет pending и uploading)
    if (uploadQueue.value.some(item => item.status === 'pending' || item.status === 'uploading')) {
        overallMessage.value = ''; // Не показываем финальное сообщение, пока идет процесс
        return;
    }

    const errorItems = uploadQueue.value.filter(item => item.status === 'error').length;
    const successItems = uploadQueue.value.filter(item => item.status === 'success').length;
    const cancelledItems = uploadQueue.value.filter(item => item.status === 'cancelled').length;

    if (successItems > 0 && errorItems === 0 && cancelledItems === 0 && uploadQueue.value.length > 0) {
        overallMessage.value = `Успешно загружено ${successItems} ${successItems === 1 ? 'файл' : successItems < 5 ? 'файла' : 'файлов'}.`;
        overallMessageType.value = 'success';
    } else if (errorItems > 0 || cancelledItems > 0) {
        let msg = 'Загрузка завершена.';
        if (successItems > 0) msg += ` Успешно: ${successItems}.`;
        if (errorItems > 0) msg += ` Ошибок: ${errorItems}.`;
        if (cancelledItems > 0) msg += ` Отменено: ${cancelledItems}.`;
        overallMessage.value = msg;
        overallMessageType.value = errorItems > 0 ? 'error' : 'success';
    } else if (successItems === 0 && errorItems === 0 && cancelledItems === 0 && uploadQueue.value.length > 0) {
        // Этого случая быть не должно при текущей логике, но на всякий случай
        overallMessage.value = 'Нет файлов для загрузки или все были отменены до начала.';
         overallMessageType.value = 'success';
    }
     else {
         overallMessage.value = ''; // Очищаем, если очередь пуста изначально
    }
};

</script>

<style scoped>
/* Стили остаются такими же, как в предыдущем ответе */
.file-upload-container { padding: 20px; max-width: 700px; margin: auto; }
h2 { margin-top: 0; margin-bottom: 20px; text-align: center; }
.drop-zone { border: 3px dashed #007bff; border-radius: 10px; padding: 40px 20px; text-align: center; cursor: pointer; background-color: #f4f8ff; transition: background-color 0.2s ease, border-color 0.2s ease; margin-bottom: 25px; }
.drop-zone.drag-over { background-color: #d6eaff; border-color: #0056b3; }
.drop-zone p { margin: 0 0 15px 0; color: #333; font-size: 1.1em; }
.file-input-label { display: inline-block; padding: 12px 25px; background-color: #007bff; color: white; border-radius: 5px; cursor: pointer; transition: background-color 0.2s; font-weight: bold; }
.file-input-label:hover { background-color: #0056b3; }
.drop-zone-hint { font-size: 0.9em; color: #6c757d; margin-top: 15px !important; }
.upload-queue { margin-top: 30px; }
.upload-queue h4 { margin-bottom: 15px; color: #333; }
.upload-queue ul { list-style: none; padding: 0; margin: 0; max-height: 350px; overflow-y: auto; border: 1px solid #e0e0e0; border-radius: 5px; padding: 10px; background-color: #fff; }
.upload-item { display: flex; justify-content: space-between; align-items: center; padding: 10px 12px; margin-bottom: 8px; border-radius: 4px; font-size: 0.95em; position: relative; overflow: hidden; border: 1px solid transparent; }
.upload-item.pending { border-color: #ddd; background-color: #f8f9fa; }
.upload-item.uploading { border-color: #bee5eb; background-color: #d1ecf1;}
.upload-item.success { border-color: #c3e6cb; background-color: #d4edda; color: #155724; }
.upload-item.error { border-color: #f5c6cb; background-color: #f8d7da; color: #721c24; }
.upload-item.cancelled { border-color: #ddd; background-color: #f8f9fa; color: #6c757d; text-decoration: line-through; }
.upload-filename { flex-grow: 1; margin-right: 15px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-family: monospace; }
.upload-status { font-weight: 500; min-width: 120px; text-align: right; font-size: 0.9em; padding-left: 10px;}
.progress-bar-container { position: absolute; left: 0; bottom: 0; width: 100%; height: 5px; background-color: rgba(0, 0, 0, 0.08); border-radius: 0 0 4px 4px;}
.progress-bar { height: 100%; background-color: #007bff; transition: width 0.1s linear; border-radius: 0 0 0 4px;}
.upload-item.success .progress-bar { background-color: #28a745;}
.upload-item.error .progress-bar { background-color: #dc3545;}
.upload-item.cancelled .progress-bar { display: none; }
.cancel-item-button { background: none; border: none; color: #dc3545; font-weight: bold; font-size: 1.4em; cursor: pointer; padding: 0 5px; margin-left: 10px; line-height: 1;}
.cancel-item-button:hover { color: #a71d2a; }
.clear-queue-button { margin-top: 15px; padding: 8px 15px; background-color: #6c757d; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 0.9em; transition: background-color 0.2s; }
.clear-queue-button:hover:not(:disabled) { background-color: #5a6268; }
.clear-queue-button:disabled { background-color: #ccc; cursor: not-allowed; }
.message { padding: 12px 15px; margin-top: 20px; border-radius: 4px; border: 1px solid transparent; }
.message.success { background-color: #d4edda; color: #155724; border-color: #c3e6cb; }
.message.error { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
</style>