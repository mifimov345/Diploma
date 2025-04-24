<template>
  <div class="file-upload-container">
    <h2>Загрузка файлов</h2>
    <div class="form-group" v-if="canUpload">
     <label for="group-select-upload">Выберите группу для загрузки:</label>
     <select id="group-select-upload" v-model="selectedGroup" required :disabled="isUploading || !currentUserGroups.length">
       <option disabled value="">-- Необходимо выбрать группу --</option>
       <option v-for="group in currentUserGroups" :key="group" :value="group">
         {{ group }}
       </option>
     </select>
     <small v-if="!currentUserGroups.length">Вы не состоите ни в одной группе.</small>
   </div>
   <div v-else class="info-message">
        <p>Вы должны состоять хотя бы в одной группе, чтобы загружать файлы.</p>
         <p v-if="currentUserRole === 'Admin' || currentUserRole === 'SuperAdmin'">Пожалуйста, <router-link :to="{ name: 'AdminGroups' }">создайте группу</router-link> или добавьте себя в существующую.</p>
         <p v-else>Обратитесь к администратору.</p>
   </div>
    <div
      class="drop-zone"
      :class="{ 'drag-over': isDragging, 'disabled': !canUpload || !selectedGroup }"
      @dragover.prevent="isDragging = true"
      @dragenter.prevent="isDragging = true"
      @dragleave.prevent="isDragging = false"
      @drop.prevent="handleDrop"
      role="button"
      :tabindex="canUpload && selectedGroup ? 0 : -1"
      @click="triggerFileInput"
      @keypress.enter="triggerFileInput"
      @keypress.space="triggerFileInput"
      :aria-disabled="!canUpload || !selectedGroup"
    >
      <p v-if="canUpload && selectedGroup">Перетащите файлы сюда или</p>
      <p v-else-if="canUpload && !selectedGroup" style="color: #dc3545;">Сначала выберите группу выше</p>
      <p v-else>Загрузка недоступна (нет групп)</p>
      <span class="file-input-label">Выберите файлы</span>
      <input
        ref="fileInputRef"
        id="file-input-hidden"
        type="file"
        multiple
        @change="handleFileSelect"
        accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,image/*,video/*"
        style="display: none"
        :disabled="!canUpload || !selectedGroup"
      />
       <p class="drop-zone-hint">Поддерживаются документы, изображения, видео. Макс. размер: 100MB.</p>
    </div>

    <div v-if="uploadQueue.length > 0" class="upload-queue">
      <h4>Очередь загрузки:</h4>
      <ul>
        <li v-for="item in uploadQueue" :key="item.id" :class="['upload-item', item.status]">
          <span class="upload-filename" :title="item.file.name">{{ item.file.name }} ({{ formatBytes(item.file.size) }})</span>
          <span class="upload-status">
            <template v-if="item.status === 'pending'">Ожидание...</template>
            <template v-else-if="item.status === 'uploading'">Загрузка {{ item.progress }}%...</template>
            <template v-else-if="item.status === 'success'">✅ Успешно</template>
            <template v-else-if="item.status === 'error'">❌ Ошибка: {{ item.errorMsg }}</template>
            <template v-else-if="item.status === 'cancelled'">Отменено</template>
            <template v-else-if="item.status === 'group_error'">⚠️ Ошибка группы</template>
          </span>
           <div v-if="item.status === 'uploading'" class="progress-bar-container">
                <div class="progress-bar" :style="{ width: item.progress + '%' }"></div>
           </div>
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

     <div v-if="overallMessage" :class="['message', overallMessageType]" role="status">
        {{ overallMessage }}
     </div>

  </div>
</template>

<script setup>
import { ref, shallowRef, nextTick, onMounted, computed } from 'vue';
import axios, { CancelToken } from 'axios';
import { formatBytes } from '@/utils/formatters';

const MAX_FILE_SIZE = 100 * 1024 * 1024;
const ALLOWED_EXTENSIONS = ['.pdf','.doc','.docx','.xls','.xlsx','.ppt','.pptx','.txt','.csv']; 
const ALLOWED_MIME_PREFIXES = ['image/', 'video/', 'text/']; 

const isDragging = ref(false);
const uploadQueue = shallowRef([]);
const overallMessage = ref('');
const overallMessageType = ref('success');
const isUploading = ref(false);
const fileInputRef = ref(null);
const currentUserGroups = ref([]); // Список групп пользователя
const selectedGroup = ref('');     // Выбранная группа для загрузки
const currentUserRole = ref('');
let nextItemId = 0;

const canUpload = computed(() => currentUserGroups.value.length > 0);

const loadCurrentUser = () => {
     try {
        currentUserRole.value = localStorage.getItem('userRole');
        currentUserGroups.value = JSON.parse(localStorage.getItem('userGroups') || '[]');
        if (currentUserGroups.value.length === 1 && !selectedGroup.value) {
             selectedGroup.value = currentUserGroups.value[0];
        }
     } catch (e) {
          overallMessage.value = "Ошибка загрузки данных пользователя. Загрузка невозможна.";
          overallMessageType.value = 'error';
          currentUserGroups.value = [];
     }
};

const triggerFileInput = () => { 
    fileInputRef.value?.click();
    if (!canUpload.value || !selectedGroup.value) return;
 };
const handleFileSelect = (event) => {
    if (!canUpload.value || !selectedGroup.value) return; 
    handleFiles(event.target.files); 
    if (event.target) event.target.value = null; 
};
const handleDrop = (event) => { 
    isDragging.value = false;
    if (!canUpload.value || !selectedGroup.value) return; 
    handleFiles(event.dataTransfer.files); 
};

const handleFiles = (fileList) => {
    if (!fileList || fileList.length === 0) return;
    if (!selectedGroup.value) {
        overallMessage.value = 'Пожалуйста, сначала выберите группу для загрузки.';
        overallMessageType.value = 'error';
        return;
    }
    overallMessage.value = '';
    let addedItems = [];
    const currentTargetGroup = selectedGroup.value;

    for (let i = 0; i < fileList.length; i++) {
        const file = fileList[i];
        const fileId = nextItemId++;
        let errorMsg = '';
        let status = 'pending'; // Начальный статус

        if (uploadQueue.value.some(item => item.file.name === file.name && item.file.size === file.size && item.status !== 'error' && item.status !== 'cancelled')) {
            //console.warn(`File skipped (duplicate): ${file.name}`);
            continue;
        }

        // Проверка размера
        if (file.size > MAX_FILE_SIZE) {
            errorMsg = `Размер > ${MAX_FILE_SIZE / 1024 / 1024}MB`;
            status = 'error';
        }
        // Проверка типа
        else {
        const extension = `.${file.name?.split('.').pop()?.toLowerCase() || ''}`;
        const mimePrefix = file.type?.split('/')[0]?.toLowerCase() + '/' || '';
        const isAllowedType = ALLOWED_EXTENSIONS.includes(extension) || ALLOWED_MIME_PREFIXES.some(prefix => mimePrefix.startsWith(prefix));

        if (!isAllowedType) {
             errorMsg = 'Неподдерживаемый тип файла';
             status = 'error';
        }

        addedItems.push({
             id: fileId,
             file: file,
             status: errorMsg ? 'error' : 'pending',
             progress: 0,
             errorMsg: errorMsg,
             targetGroup: currentTargetGroup,
             cancelTokenSource: null
         });
    }
}

    if (addedItems.length > 0) {
        uploadQueue.value = [...addedItems, ...uploadQueue.value];
        if (!isUploading.value) {
             processQueue();
         }
    } else if (uploadQueue.value.some(item => item.status === 'error')) {
         overallMessage.value = 'Некоторые файлы не соответствуют требованиям.';
         overallMessageType.value = 'error';
    }
};

const processQueue = async () => {
    const pendingItemIndex = uploadQueue.value.findIndex(item => item.status === 'pending');

    if (pendingItemIndex === -1) {
        const currentlyUploading = uploadQueue.value.some(item => item.status === 'uploading');
        isUploading.value = currentlyUploading;
        if (!isUploading.value && pendingItemIndex === -1) {
             updateOverallMessage();
        }
        return;
    }

    if (isUploading.value) return;

    isUploading.value = true;
    const currentItem = uploadQueue.value[pendingItemIndex];

    if (!currentItem.targetGroup) {
         currentItem.status = 'group_error';
         currentItem.errorMsg = 'Группа не была выбрана для этого файла.';
         isUploading.value = false;
        await nextTick();
        processQueue(); // Переходим к следующему
         return;
    }

    currentItem.status = 'uploading';
    currentItem.progress = 0;
    currentItem.errorMsg = '';
    currentItem.cancelTokenSource = CancelToken.source();

    const formData = new FormData();
    formData.append("file", currentItem.file);
    formData.append("targetGroup", currentItem.targetGroup);

    try {
        const apiUrl = '/api/file/upload';
        const response = await axios.post(apiUrl, formData, {
            headers: { 'Content-Type': 'multipart/form-data' },
            cancelToken: currentItem.cancelTokenSource.token,
            onUploadProgress: (progressEvent) => {
                 if (currentItem.status === 'uploading') {
                      const percentCompleted = progressEvent.total
                        ? Math.round((progressEvent.loaded * 100) / progressEvent.total)
                        : 50;
                      currentItem.progress = Math.min(percentCompleted, 100);
                 }
            }
        });
        console.log(`Upload Success for ${currentItem.file.name}. Response Status: ${response.status}`);
         if (currentItem.status === 'uploading') {
            console.log(`[BEFORE SUCCESS] Item ${currentItem.file.name}: status=${currentItem.status}, errorMsg='${currentItem.errorMsg}'`);
             currentItem.status = 'success';
             currentItem.progress = 100;
             //console.log(`File ${currentItem.file.name} uploaded successfully:`, response.data);
            console.log(`[AFTER SUCCESS] Item ${currentItem.file.name}: status=${currentItem.status}, errorMsg='${currentItem.errorMsg}'`);

         }

    } catch (err) {
        if (axios.isCancel(err)) {
            currentItem.status = 'cancelled';
        } else {
            //console.error(`Error uploading file ${currentItem.file.name}:`, err);
            console.error(`Error uploading file ${currentItem.file.name}:`, err.toJSON ? err.toJSON() : err);
            if (currentItem.status !== 'cancelled') {
                console.log(`[BEFORE ERROR] Item ${currentItem.file.name}: status=${currentItem.status}, errorMsg='${currentItem.errorMsg}'`);
                if (currentItem.status === 'uploading') {
                    currentItem.status = 'error';
                 }
                 if (err.response){
                     currentItem.errorMsg = err.response.data?.detail || err.response.data?.title || err.response.data || `Ошибка ${err.response.status}`; 
                 } else if (err.request){
                    currentItem.errorMsg = 'Нет ответа от сервера';
                 } else {
                    currentItem.errorMsg = 'Ошибка при отправке запроса';
                 }
            }
        }
    } finally {
         currentItem.cancelTokenSource = null;
         isUploading.value = false;
         await nextTick();
         processQueue();
    }
};

const cancelUpload = (itemId) => {
    const itemIndex = uploadQueue.value.findIndex(item => item.id === itemId);
    if (itemIndex === -1) return;
    const item = uploadQueue.value[itemIndex];
    if (item) {
         if (item.status === 'uploading' && item.cancelTokenSource) {
              item.cancelTokenSource.cancel('Upload cancelled by user.');
         } else if (item.status === 'pending') {
            uploadQueue.value = [
            ...uploadQueue.value.slice(0, itemIndex),
                { ...item, status: 'cancelled' },
                ...uploadQueue.value.slice(itemIndex + 1),
            ];
         }
    }
};


const clearQueue = () => {
    uploadQueue.value = uploadQueue.value.filter(item => item.status === 'pending' || item.status === 'uploading');
    overallMessage.value = '';
    if (!uploadQueue.value.some(item => item.status === 'pending' || item.status === 'uploading')) {
        isUploading.value = false;
    }
};

const updateOverallMessage = () => {
    if (uploadQueue.value.some(item => item.status === 'pending' || item.status === 'uploading')) {
        overallMessage.value = '';
        return;
    }

    const errorItems = uploadQueue.value.filter(item => item.status === 'error').length;
    const successItems = uploadQueue.value.filter(item => item.status === 'success').length;
    const cancelledItems = uploadQueue.value.filter(item => item.status === 'cancelled').length;
    const totalProcessed = errorItems + successItems + cancelledItems;
    if (totalProcessed === 0) {
         overallMessage.value = '';
         return;
     }

    if (successItems > 0 && errorItems === 0 && cancelledItems === 0 && uploadQueue.value.length > 0) {
        overallMessage.value = `Успешно загружено: ${successItems} ${getNoun(successItems, 'файл', 'файла', 'файлов')}.`;
        overallMessageType.value = 'success';
    } else {
        let msg = 'Загрузка завершена.';
        if (successItems > 0) msg += ` Успешно: ${successItems}.`;
        if (errorItems > 0) msg += ` Ошибок: ${errorItems}.`;
        if (cancelledItems > 0) msg += ` Отменено: ${cancelledItems}.`;
        overallMessage.value = msg;
        overallMessageType.value = errorItems > 0 ? 'error' : 'success';
    }
};

// Хелпер для склонения существительных
const getNoun = (number, one, two, five) => {
    let n = Math.abs(number);
    n %= 100;
    if (n >= 5 && n <= 20) { return five; }
    n %= 10;
    if (n === 1) { return one; }
    if (n >= 2 && n <= 4) { return two; }
    return five;
}

onMounted(() => {
    loadCurrentUser();
});

</script>

<style scoped>
 .file-upload-container { padding: 20px; max-width: 700px; margin: auto; }
 h2 { margin-top: 0; margin-bottom: 20px; text-align: center; }
 .form-group { margin-bottom: 20px; } /* Стиль для селекта группы */
 .form-group label { display: block; margin-bottom: 8px; font-weight: bold; }
 .form-group select { width: 100%; padding: 10px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; }
 .form-group select:disabled { background-color: #e9ecef; cursor: not-allowed; }
 .form-group small { display: block; margin-top: 5px; font-size: 0.85em; color: #6c757d; }
 
 .drop-zone { border: 3px dashed #007bff; border-radius: 10px; padding: 40px 20px; text-align: center; cursor: pointer; background-color: #f4f8ff; transition: background-color 0.2s ease, border-color 0.2s ease; margin-bottom: 25px; }
 .drop-zone.drag-over { background-color: #d6eaff; border-color: #0056b3; }
 .drop-zone.disabled { border-color: #ced4da; background-color: #e9ecef; cursor: not-allowed; }
 .drop-zone.disabled p, .drop-zone.disabled .file-input-label { color: #6c757d; }
 .drop-zone.disabled .file-input-label { background-color: #adb5bd; }

 .drop-zone p { margin: 0 0 15px 0; color: #333; font-size: 1.1em; }
 .file-input-label { display: inline-block; padding: 12px 25px; background-color: #007bff; color: white; border-radius: 5px; cursor: pointer; transition: background-color 0.2s; font-weight: bold; }
 .file-input-label:not(.disabled):hover { background-color: #0056b3; }
 .file-input-label.disabled { background-color: #adb5bd; cursor: not-allowed; }

 .drop-zone-hint { font-size: 0.9em; color: #6c757d; margin-top: 15px !important; }
 .upload-queue { margin-top: 30px; }
 .upload-queue h4 { margin-bottom: 15px; color: #333; }
 .upload-item.pending { border-color: #ddd; background-color: #f8f9fa; }
 .upload-item.uploading { border-color: #bee5eb; background-color: #d1ecf1;}
 .upload-item.success { border-color: #c3e6cb; background-color: #d4edda; color: #155724; }
 .upload-item.group_error { border-color: #ffc107; background-color: #fff3cd; color: #856404; }
 .upload-item.error { border-color: #f5c6cb; background-color: #f8d7da; color: #721c24; }
 .upload-item.cancelled { border-color: #ddd; background-color: #f8f9fa; color: #6c757d; text-decoration: line-through; }
 .upload-filename { flex-grow: 1; margin-right: 15px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-family: monospace; }
 .message { padding: 12px 15px; margin-top: 20px; border-radius: 4px; border: 1px solid transparent; }
 .message.success { background-color: #d4edda; color: #155724; border-color: #c3e6cb; }
 .message.error { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
 .info-message { padding: 15px; margin-bottom: 20px; border-radius: 4px; border: 1px solid #d6d8db; background-color: #e2e3e5; color: #383d41;}
 </style>