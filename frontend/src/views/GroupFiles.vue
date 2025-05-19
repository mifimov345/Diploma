<template>
  <div class="group-files-view">
    <h2>Файлы группы</h2>
    <p v-if="currentUserGroups.length > 0">Отображаются файлы из ваших групп: <strong>{{ currentUserGroups.join(', ') }}</strong></p>
    <p v-else>Вы не состоите ни в одной группе, поэтому здесь нет файлов.</p>

     <div class="search-container" v-if="currentUserGroups.length > 0">
        <input
          type="search"
          v-model="searchQuery"
          placeholder="Поиск по имени файла в группах..."
          @input="debouncedSearchFiles"
          :disabled="isLoading || isLoadingSearch"
          aria-label="Поиск по файлам группы"
        />
        <button @click="clearSearch" v-if="searchQuery" class="clear-search-button" title="Очистить поиск" aria-label="Очистить поиск">×</button>
     </div>

    <div class="controls" v-if="currentUserGroups.length > 0">
       <button @click="fetchGroupFiles" :disabled="isLoading" class="refresh-button">
        <span v-if="isLoading && !isLoadingSearch">Обновление...</span>
        <span v-else>Обновить список</span>
      </button>
       <span v-if="isLoadingSearch" class="loading-indicator small">Поиск...</span>
    </div>

    <div v-if="error" class="error-message" role="alert"> {{ error }} </div>

    <div v-if="currentUserGroups.length > 0 && (!isLoading || filteredFiles.length > 0)" class="file-list-container" aria-live="polite">
        <ul v-if="filteredFiles.length > 0" class="file-list no-bullets">
          <FileListItem
              v-for="file in filteredFiles"
              :key="file.Id"
              :file="file"
              :is-action-in-progress="isActionInProgress(file.Id)"
              :action-type="getActionType(file.Id)"
              :show-delete-button="false" 
              :show-owner-info="true" 
              @download-file="downloadFileFromList"
              @click="openPreviewModal(file)" 
              class="clickable-list-item"
          >
              <template #actions>
                  <button @click.stop="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр" aria-label="Предпросмотр файла">👁️</button>
              </template>
              <template #meta>
                 | Тип: {{ file.ContentType || 'N/A' }}
              </template>
          </FileListItem>
        </ul>
         <div v-else-if="!isLoading && !isLoadingSearch && !error" class="no-files-message">
            <p>{{ isUsingSearchResults ? 'Файлы не найдены по вашему запросу в группах.' : 'В ваших группах пока нет файлов (кроме ваших собственных).' }}</p>
         </div>
    </div>
     <div v-else-if="isLoading && !isLoadingSearch && currentUserGroups.length > 0" class="loading-indicator"> Загрузка файлов группы... </div>

    <FilePreviewModal
        v-if="showPreviewModal"
        :file-id="previewFileDetails.Id"
        :content-type="previewFileDetails.ContentType"
        :original-name="previewFileDetails.OriginalName"
        @close="closePreviewModal"
        @download-original="downloadFileFromPreview"
     />

  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import axios from 'axios';
import FileListItem from '@/components/files/FileListItem.vue';
import FilePreviewModal from '@/components/files/FilePreviewModal.vue';

const files = ref([]);
const searchResultsById = ref([]);
const searchQuery = ref('');
const isUsingSearchResults = ref(false);
const isLoading = ref(false);
const isLoadingSearch = ref(false);
const isDownloading = ref(null);
const error = ref('');
const searchTimeout = ref(null);
const showPreviewModal = ref(false);
const previewFileDetails = ref(null);
const currentUserId = ref(null);
const currentUserGroups = ref([]);

const filteredFiles = computed(() => {
    if (!isUsingSearchResults.value) {
        return files.value;
    }
    if (searchResultsById.value.length === 0 && searchQuery.value) {
        return [];
    }
    const searchIdSet = new Set(searchResultsById.value);
    return files.value.filter(file => searchIdSet.has(file.Id));
});

const isActionInProgress = computed(() => (fileId) => isDownloading.value === fileId);

const getActionType = (fileId) => {
  if (isDownloading.value === fileId) return 'download';
  return null;
};

const loadCurrentUser = () => {
     try {
        currentUserId.value = parseInt(localStorage.getItem('userId') || '0');
        currentUserGroups.value = JSON.parse(localStorage.getItem('userGroups') || '[]');
        if (isNaN(currentUserId.value) || currentUserId.value <= 0) { throw new Error("Invalid userId"); }
     } catch (e) {
          error.value = "Ошибка загрузки данных пользователя.";
          currentUserGroups.value = []; // Сбрасываем группы при ошибке
     }
};

// Запрашиваем файлы группы
const fetchGroupFiles = async () => {
    if (isLoading.value || currentUserGroups.value.length === 0) return;
    isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
    try {
        const response = await axios.get('/api/file/files', { params: { scope: 'group' } });
        files.value = response.data || [];
    } catch (err) {
        files.value = [];
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else if (err.response && err.response.status === 403) { error.value = 'Доступ к файлам группы запрещен.'; }
        else { error.value = 'Не удалось загрузить список файлов группы.'; }
    } finally { isLoading.value = false; }
};

// Поиск по файлам группы
const performSearch = async () => {
    if (!searchQuery.value || currentUserGroups.value.length === 0) {
        searchResultsById.value = []; isUsingSearchResults.value = false; error.value = ''; return;
    }
    isLoadingSearch.value = true; error.value = ''; isUsingSearchResults.value = true;
    try {
        const response = await axios.get(`/api/search`, { params: { term: searchQuery.value, scope: 'group' } });
        searchResultsById.value = response.data || [];

    } catch (err) {
        searchResultsById.value = [];
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else { error.value = 'Ошибка при поиске файлов группы.'; }
    } finally { isLoadingSearch.value = false; }
};

const debouncedSearchFiles = () => {
    clearTimeout(searchTimeout.value);
    searchTimeout.value = setTimeout(performSearch, 500);
};

const clearSearch = () => {
    searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false; error.value = '';
};

const downloadFileFromList = (fileId) => {
    const file = filteredFiles.value.find(f => f.Id === fileId);
    if (file) {
        downloadFile(fileId, file.OriginalName); // Вызываем старую функцию скачивания
    } else {
         console.error(`GroupFiles: FileListItem download request for unknown ID: ${fileId}`);
         error.value = `Не удалось найти информацию для скачивания файла ID: ${fileId}`;
    }
};

const downloadFile = async (fileId, originalName) => {
    if (isDownloading.value) return;
    isDownloading.value = fileId; error.value = '';
    try {
      const response = await axios.get(`/api/file/download/${fileId}`, { responseType: 'blob' });
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', originalName || `file_${fileId}`);
      document.body.appendChild(link); link.click();
      window.URL.revokeObjectURL(url); link.remove();
    } catch (err) {
      //console.error(`Error downloading file ${fileId}:`, err);
      let downloadError = `Не удалось скачать файл "${originalName || fileId}".`;
      if (err.response) { /* ... обработка 404, 403, 401 ... */ }
      error.value = downloadError;
    } finally { isDownloading.value = null; }
};

const openPreviewModal = (file) => {
    //console.log('Opening preview. File object:', file);
    //console.log('Content Type being passed:', file.ContentType);
     if (!file || !file.Id) {
         //console.error("Invalid file object passed to openPreviewModal:", file);
         return;
     }
    previewFileDetails.value = {
        Id: file.Id,
        ContentType: file.ContentType,
        OriginalName: file.OriginalName
    };
    showPreviewModal.value = true;
};

const closePreviewModal = () => {
    showPreviewModal.value = false;
    previewFileDetails.value = null;
};

const downloadFileFromPreview = (fileId) => {
     const file = filteredFiles.value.find(f => f.Id === fileId);
     downloadFile(fileId, file ? file.OriginalName : `file_${fileId}`);
};

onMounted(() => {
    loadCurrentUser();
    if (currentUserId.value && currentUserGroups.value.length > 0) {
        fetchGroupFiles();
    }
});
onBeforeUnmount(() => { clearTimeout(searchTimeout.value); });

</script>

<style scoped>
.file-list.no-bullets {
  list-style-type: none;
  padding-left: 0;
}
.clickable-list-item {
    cursor: pointer;
}.group-files-view { padding: 20px; background-color: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08); }
h2 { margin-top: 0; margin-bottom: 15px; }
p { margin-bottom: 20px; color: #555; }

.action-button {
    background: none;
    border: none;
    padding: 5px;
    cursor: pointer;
    font-size: 1.2rem;
    border-radius: 4px;
    line-height: 1;
    transition: transform 0.1s ease, color 0.2s ease;
    min-width: 30px;
    min-height: 30px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
}
.action-button:hover:not(:disabled) { transform: scale(1.1); }
.action-button:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
.preview-button { color: #6f42c1; }
.download-button { color: #007bff; }

.search-container { margin-bottom: 20px; position: relative; max-width: 450px; }
.search-container input[type="search"] { width: 100%; padding: 10px 35px 10px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; }
.clear-search-button { position: absolute; right: 5px; top: 50%; transform: translateY(-50%); background: none; border: none; font-size: 1.6rem; cursor: pointer; color: #aaa; }
.controls { margin-bottom: 20px; display: flex; align-items: center; gap: 15px; min-height: 38px; }
.refresh-button { padding: 10px 18px; background-color: #17a2b8; color: white; border: none; border-radius: 4px; cursor: pointer; }
.loading-indicator.small { margin-left: 10px; color: #6c757d; font-style: italic; }
.error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; padding: 15px; border-radius: 4px; margin-bottom: 20px; }
.file-list-container { min-height: 100px; }
.file-meta strong { font-weight: 600; color: #555; }
.action-button:disabled { opacity: 0.5; cursor: not-allowed; }
.no-files-message { text-align: center; padding: 20px; background-color: #e9ecef; color: #495057; border-radius: 4px; }
.loading-indicator { text-align: center; padding: 20px; color: #6c757d; }
</style>