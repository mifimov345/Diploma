<template>
  <div class="my-files-view">
    <h2>Мои файлы</h2>

    <!-- Поиск -->
    <div class="search-container">
        <input
          type="search"
          v-model="searchQuery"
          placeholder="Поиск по имени файла..."
          @input="debouncedSearchFiles"
          :disabled="isLoading || isLoadingSearch"
          aria-label="Поиск по моим файлам"
        />
        <button @click="clearSearch" v-if="searchQuery" class="clear-search-button" title="Очистить поиск" aria-label="Очистить поиск">×</button>
    </div>

    <!-- Контролы -->
    <div class="controls">
       <button @click="fetchMyFiles" :disabled="isLoading" class="refresh-button">
        <span v-if="isLoading && !isLoadingSearch">Обновление...</span>
        <span v-else>Обновить список</span>
      </button>
       <span v-if="isLoadingSearch" class="loading-indicator small">Поиск...</span>
    </div>

    <!-- Сообщение об ошибке -->
    <div v-if="error" class="error-message" role="alert"> {{ error }} </div>

    <!-- Список файлов -->
    <div v-if="!isLoading || filteredFiles.length > 0" class="file-list-container" aria-live="polite">
        <ul v-if="filteredFiles.length > 0" class="file-list">
          <li v-for="file in filteredFiles" :key="file.Id" class="file-item">
             <div class="file-icon" :aria-label="`Тип файла: ${file.ContentType || 'Неизвестный'}`">📄</div> <!-- Можно улучшить иконку -->
             <div class="file-details">
               <span class="file-name" :title="file.OriginalName">{{ file.OriginalName }}</span>
               <span class="file-meta">
                 Размер: {{ formatBytes(file.SizeBytes) }} |
                 Загружен: {{ formatDate(file.UploadedAt) }} |
                 Тип: {{ file.ContentType || 'N/A' }}
               </span>
             </div>
             <div class="file-actions">
                <button @click="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр" aria-label="Предпросмотр файла">👁️</button>
                <button @click="downloadFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать файл" aria-label="Скачать файл"> <span v-if="isDownloading === file.Id">...</span><span v-else>📥</span> </button>
                <!-- <button @click="deleteFile(file.Id, file.OriginalName)" ...>🗑️</button> -->
             </div>
          </li>
        </ul>
         <!-- Сообщение, если список пуст -->
         <div v-else-if="!isLoading && !isLoadingSearch && !error" class="no-files-message">
            <p>{{ isUsingSearchResults ? 'Файлы не найдены по вашему запросу.' : 'У вас пока нет загруженных файлов.' }}</p>
         </div>
    </div>
    <!-- Индикатор загрузки полного списка -->
     <div v-else-if="isLoading && !isLoadingSearch" class="loading-indicator"> Загрузка файлов... </div>

    <!-- Модальное окно предпросмотра -->
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
import { formatBytes, formatDate } from '@/utils/formatters';
import FilePreviewModal from '@/components/files/FilePreviewModal.vue';

// --- Состояние ---
const files = ref([]); // Все файлы пользователя
const searchResultsById = ref([]); // ID найденных файлов
const searchQuery = ref('');
const isUsingSearchResults = ref(false); // Флаг активности поиска
const isLoading = ref(false); // Загрузка списка
const isLoadingSearch = ref(false); // Загрузка поиска
const isDownloading = ref(null); // ID скачиваемого файла
const error = ref('');
const searchTimeout = ref(null);
const showPreviewModal = ref(false);
const previewFileDetails = ref(null);
const currentUserId = ref(null); // ID текущего пользователя

// --- Computed ---
// Определяем, какой список отображать (полный или результаты поиска)
const filteredFiles = computed(() => {
    if (!isUsingSearchResults.value) {
        return files.value; // Поиск не активен, показываем все
    }
    // Если поиск был, но результатов нет
    if (searchResultsById.value.length === 0 && searchQuery.value) {
        return [];
    }
    // Фильтруем основной список по ID из результатов поиска
    const searchIdSet = new Set(searchResultsById.value);
    return files.value.filter(file => searchIdSet.has(file.Id));
});

// Определяем, идет ли какое-то действие с файлом
const isActionInProgress = computed(() => (fileId) => isDownloading.value === fileId); // Можно добавить isDeleting

// --- Методы ---
const loadCurrentUser = () => {
     try {
        currentUserId.value = parseInt(localStorage.getItem('userId') || '0');
        if (isNaN(currentUserId.value) || currentUserId.value <= 0) {
            console.error("MyFilesView: Invalid currentUserId loaded from localStorage."); // <-- Срабатывает этот блок
            error.value = "Не удалось определить пользователя.";
        }
     } catch (e) {
          console.error("MyFilesView: Error parsing userId from localStorage.", e);
          error.value = "Ошибка загрузки данных пользователя.";
     }
};

const fetchMyFiles = async () => {
    if (isLoading.value) return; // Защита от двойного запроса
    isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
    try {
        // Запрашиваем файлы, бэкенд сам отфильтрует для текущего пользователя
        const response = await axios.get('/api/file/files');
        files.value = response.data || [];
    } catch (err) {
        console.error('Error fetching my files:', err);
        files.value = []; // Очищаем при ошибке
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else { error.value = 'Не удалось загрузить список файлов.'; }
    } finally { isLoading.value = false; }
};

const performSearch = async () => {
    if (!searchQuery.value) { // Если строка поиска пуста
        searchResultsById.value = [];
        isUsingSearchResults.value = false; // Выключаем режим поиска
        error.value = '';
        return;
    }
    if (!currentUserId.value) { // Проверяем, загружен ли ID пользователя
         error.value = "Не удалось определить ID пользователя для поиска.";
         return;
    }
    isLoadingSearch.value = true; error.value = ''; isUsingSearchResults.value = true;
    try {
        // Передаем userId для фильтрации на бэкенде
        const response = await axios.get(`/api/search`, { params: { term: searchQuery.value, userId: currentUserId.value } });
        searchResultsById.value = response.data || []; // Ожидаем массив Guid
    } catch (err) {
        console.error('Error searching files:', err);
        searchResultsById.value = [];
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else { error.value = 'Ошибка при поиске файлов.'; }
    } finally { isLoadingSearch.value = false; }
};

const debouncedSearchFiles = () => {
    clearTimeout(searchTimeout.value);
    searchTimeout.value = setTimeout(performSearch, 500);
};

const clearSearch = () => {
    searchQuery.value = '';
    searchResultsById.value = [];
    isUsingSearchResults.value = false;
    error.value = '';
    // Обновлять ли список здесь? filteredFiles сам переключится на files.value
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
      console.error(`Error downloading file ${fileId}:`, err);
      let downloadError = `Не удалось скачать файл "${originalName || fileId}".`;
      if (err.response) { /* ... обработка 404, 403, 401 ... */ }
      error.value = downloadError;
    } finally { isDownloading.value = null; }
};

const openPreviewModal = (file) => {
     if (!file || !file.Id) {
         console.error("Invalid file object passed to openPreviewModal:", file);
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
     // Ищем в текущем отфильтрованном списке
     const file = filteredFiles.value.find(f => f.Id === fileId);
     downloadFile(fileId, file ? file.OriginalName : `file_${fileId}`);
};

// --- Lifecycle ---
onMounted(() => {
    loadCurrentUser(); // Сначала загружаем ID
    if(currentUserId.value) { // Загружаем файлы только если ID есть
        fetchMyFiles();
    } else {
         console.error("MyFilesView: Cannot fetch files, currentUserId is missing.");
         // Ошибка уже должна быть установлена в loadCurrentUser
    }
});

onBeforeUnmount(() => {
    clearTimeout(searchTimeout.value); // Очищаем таймер
});

</script>

<style scoped>
.my-files-view { padding: 20px; background-color: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08); }
h2 { margin-top: 0; margin-bottom: 25px; color: #333; border-bottom: 1px solid #eee; padding-bottom: 10px; }

/* Стили Поиска */
.search-container { margin-bottom: 20px; position: relative; max-width: 450px; }
.search-container input[type="search"] { width: 100%; padding: 10px 35px 10px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; font-size: 0.95rem; }
.clear-search-button { position: absolute; right: 5px; top: 50%; transform: translateY(-50%); background: none; border: none; font-size: 1.6rem; font-weight: bold; cursor: pointer; color: #aaa; padding: 0 5px; line-height: 1; }
.clear-search-button:hover { color: #555; }

/* Стили контролов */
.controls { margin-bottom: 20px; display: flex; align-items: center; gap: 15px; min-height: 38px; /* Чтобы не прыгало при появлении лоадера */ }
.refresh-button { padding: 10px 18px; background-color: #17a2b8; color: white; border: none; border-radius: 4px; cursor: pointer; transition: background-color 0.2s; }
.refresh-button:hover:not(:disabled) { background-color: #138496; }
.refresh-button:disabled { background-color: #cccccc; cursor: not-allowed; }
.loading-indicator.small { display: inline-block; padding: 0; margin: 0; margin-left: 10px; color: #6c757d; font-style: italic; }

/* Индикаторы и сообщения */
.loading-indicator, .no-files-message, .error-message { padding: 20px; margin-top: 20px; border-radius: 4px; text-align: center; }
.loading-indicator { color: #6c757d; }
.no-files-message { background-color: #e9ecef; color: #495057; }
.error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
.file-list-container { min-height: 100px; /* Предотвращаем скачки высоты */}

/* Список файлов */
.file-list { list-style: none; padding: 0; margin: 0; }
.file-item { display: flex; align-items: center; padding: 12px 15px; border: 1px solid #e9ecef; border-radius: 5px; margin-bottom: 10px; background-color: #fff; transition: box-shadow 0.2s ease; }
.file-item:hover { box-shadow: 0 2px 5px rgba(0,0,0,0.1); }
.file-icon { font-size: 1.8rem; margin-right: 15px; color: #6c757d; width: 30px; text-align: center; }
.file-details { flex-grow: 1; display: flex; flex-direction: column; overflow: hidden; min-width: 0;}
.file-name { font-weight: 600; color: #343a40; margin-bottom: 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: default; }
.file-meta { font-size: 0.8rem; color: #6c757d; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.file-actions { margin-left: 15px; display: flex; align-items: center; gap: 8px; }
.action-button { background: none; border: none; padding: 5px; cursor: pointer; font-size: 1.2rem; border-radius: 4px; line-height: 1; transition: transform 0.1s ease, color 0.2s ease; min-width: 30px; min-height: 30px; display: inline-flex; align-items: center; justify-content: center; }
.action-button:hover:not(:disabled) { transform: scale(1.1); }
.action-button:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
.preview-button { color: #6f42c1; }
.download-button { color: #007bff; }
.delete-button { color: #dc3545; }
</style>