<template>
  <div class="admin-file-browser">
    <h2>Обзор файлов</h2> <!-- Изменил заголовок -->

     <!-- Поиск -->
     <div class="search-container">
        <input
          type="search"
          v-model="searchQuery"
          placeholder="Поиск по имени файла во всех файлах..."
          @input="debouncedSearchFiles"
          :disabled="isLoading || isLoadingSearch"
          aria-label="Поиск по всем файлам"
        />
        <button @click="clearSearch" v-if="searchQuery" class="clear-search-button" title="Очистить поиск" aria-label="Очистить поиск">×</button>
     </div>

    <!-- Контролы -->
    <div class="controls">
       <button @click="fetchAllFiles" :disabled="isLoading" class="refresh-button">
         <span v-if="isLoading && !isLoadingSearch">Обновление...</span>
         <span v-else>Обновить список</span>
       </button>
       <span v-if="isLoadingSearch" class="loading-indicator small">Поиск...</span>
    </div>

     <!-- Сообщение об ошибке -->
     <div v-if="error" class="error-message" role="alert">{{ error }}</div>

     <!-- Список файлов: либо результаты поиска, либо сгруппированный полный список -->
      <div v-if="!isLoading || groupedAndFilteredFiles.length > 0" class="file-list-container" aria-live="polite">
           <!-- Отображаем результаты поиска как одну группу -->
           <div v-if="isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-group search-results-group">
               <h3>Результаты поиска по запросу: "{{ searchQuery }}" (Найдено: {{ filteredFiles.length }})</h3>
               <ul class="file-list">
                   <li v-for="file in filteredFiles" :key="file.Id" class="file-item">
                       <!-- Содержимое file-item (лучше вынести в компонент) -->
                        <div class="file-icon">📄</div>
                         <div class="file-details">
                            <span class="file-name" :title="file.OriginalName">{{ file.OriginalName }}</span>
                            <span class="file-meta">
                               User ID: {{ file.UserId }} | Группа: {{ file.UserGroup }} | Размер: {{ formatBytes(file.SizeBytes) }} | Загружен: {{ formatDate(file.UploadedAt) }} | Тип: {{ file.ContentType || 'N/A' }}
                            </span>
                        </div>
                        <div class="file-actions">
                             <button @click="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр" aria-label="Предпросмотр файла">👁️</button>
                             <button @click="downloadFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать файл" aria-label="Скачать файл"> <span v-if="isDownloading === file.Id">...</span><span v-else>📥</span> </button>
                             <button @click="deleteFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button delete-button" title="Удалить файл" aria-label="Удалить файл">🗑️</button>
                        </div>
                   </li>
               </ul>
           </div>

           <!-- Иначе показываем с группировкой по пользователю/группе -->
           <div v-else-if="!isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-groups-container">
               <div v-for="group in groupedAndFilteredFiles" :key="group.key" class="file-group">
                 <h3>Группа: <span class="group-name">{{ group.groupName }}</span> | Пользователь ID: <span class="user-id">{{ group.userId }}</span> ({{ group.files.length }} файлов)</h3>
                 <ul class="file-list">
                   <li v-for="file in group.files" :key="file.Id" class="file-item">
                      <!-- Содержимое file-item -->
                        <div class="file-icon">📄</div>
                         <div class="file-details">
                            <span class="file-name" :title="file.OriginalName">{{ file.OriginalName }}</span>
                            <span class="file-meta">
                               Размер: {{ formatBytes(file.SizeBytes) }} | Загружен: {{ formatDate(file.UploadedAt) }} | Тип: {{ file.ContentType || 'N/A' }}
                               <!-- User ID и Group уже есть в заголовке группы -->
                            </span>
                        </div>
                         <div class="file-actions">
                             <button @click="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр" aria-label="Предпросмотр файла">👁️</button>
                             <button @click="downloadFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать файл" aria-label="Скачать файл"> <span v-if="isDownloading === file.Id">...</span><span v-else>📥</span> </button>
                             <button @click="deleteFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button delete-button" title="Удалить файл" aria-label="Удалить файл">🗑️</button>
                         </div>
                   </li>
                 </ul>
               </div>
           </div>

            <!-- Сообщение, если список пуст -->
          <div v-else-if="!isLoading && !isLoadingSearch && !error" class="no-files-message">
              <p>{{ isUsingSearchResults ? 'Файлы не найдены по вашему запросу.' : 'Пока не загружено ни одного файла.' }}</p>
          </div>
      </div>
       <!-- Индикатор загрузки полного списка -->
      <div v-else-if="isLoading && !isLoadingSearch" class="loading-indicator">Загрузка списка файлов...</div>


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
const allFiles = ref([]); // Все файлы с бэкенда
const searchResultsById = ref([]); // Массив ID из поиска
const searchQuery = ref('');
const isUsingSearchResults = ref(false); // Флаг активности поиска
const isLoading = ref(false); // Загрузка полного списка
const isLoadingSearch = ref(false); // Загрузка поиска
const isDownloading = ref(null);
const error = ref('');
const searchTimeout = ref(null);
const showPreviewModal = ref(false);
const previewFileDetails = ref(null);

// --- Computed ---
// Фильтруем основной список по результатам поиска (если поиск активен)
const filteredFiles = computed(() => {
  if (!isUsingSearchResults.value) return allFiles.value;
  if (searchResultsById.value.length === 0 && searchQuery.value) return [];
  const searchIdSet = new Set(searchResultsById.value);
  return allFiles.value.filter(file => searchIdSet.has(file.Id));
});

// Группируем отфильтрованные файлы для отображения
const groupedAndFilteredFiles = computed(() => {
  // Если поиск активен, возвращаем одну группу с результатами
  if (isUsingSearchResults.value) {
      if (!filteredFiles.value || filteredFiles.value.length === 0) return [];
      return [{ key: 'search-results', files: filteredFiles.value }];
  }
  // Иначе группируем полный список по пользователю и группе
  const groups = {};
  allFiles.value.forEach(file => {
    const groupKey = `user-${file.UserId}_group-${file.UserGroup || 'nogroup'}`;
    if (!groups[groupKey]) { groups[groupKey] = { key: groupKey, userId: file.UserId, groupName: file.UserGroup || 'Без группы', files: [] }; }
    groups[groupKey].files.push(file);
  });
  // Сортировка групп
  return Object.values(groups).sort((a, b) => {
      if (a.userId !== b.userId) return a.userId - b.userId;
      return (a.groupName || '').localeCompare(b.groupName || '');
  });
});

const isActionInProgress = computed(() => (fileId) => isDownloading.value === fileId);

// --- Методы ---
const fetchAllFiles = async () => {
  if (isLoading.value) return;
  isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
  try {
      const response = await axios.get('/api/file/files'); // Админ получает все файлы
      allFiles.value = response.data || [];
  } catch (err) {
      console.error('Error fetching all files:', err);
      allFiles.value = [];
      if (err.response) {
           if (err.response.status === 403) error.value = 'Доступ запрещен.';
           else if (err.response.status === 401) error.value = 'Сессия истекла.';
           else error.value = 'Не удалось загрузить список файлов.';
      } else { error.value = 'Ошибка сети.'; }
  } finally { isLoading.value = false; }
};

const performSearch = async () => {
  if (!searchQuery.value) { searchResultsById.value = []; isUsingSearchResults.value = false; error.value = ''; return; }
  isLoadingSearch.value = true; error.value = ''; isUsingSearchResults.value = true;
  try {
      // Админ ищет по всем файлам, userId не передаем
      const response = await axios.get(`/api/search`, { params: { term: searchQuery.value } });
      searchResultsById.value = response.data || []; // Ожидаем массив Guid
  } catch (err) {
      console.error('Error searching files:', err);
      searchResultsById.value = [];
      if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
      else { error.value = 'Ошибка при поиске файлов.'; }
  } finally { isLoadingSearch.value = false; }
};

const debouncedSearchFiles = () => { clearTimeout(searchTimeout.value); searchTimeout.value = setTimeout(performSearch, 500); };
const clearSearch = () => { searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false; error.value = ''; };
const downloadFile = async (fileId, originalName) => { /* ... как в MyFilesView ... */
  if (isDownloading.value) return;
  isDownloading.value = fileId; error.value = '';
  try { /* ... axios get blob ... create url ... click link ... */ }
  catch (err) { /* ... обработка ошибок 404, 403, 401 ... */ }
  finally { isDownloading.value = null; }
};
const deleteFile = async (fileId, originalName) => {
   if (!confirm(`Уверены, что хотите удалить файл "${originalName}"?`)) return;
   error.value = '';
   // Добавить индикатор isDeleting, если нужно
   try {
       await axios.delete(`/api/file/files/${fileId}`); // Админский эндпоинт
       allFiles.value = allFiles.value.filter(f => f.Id !== fileId);
       if(isUsingSearchResults.value) {
            searchResultsById.value = searchResultsById.value.filter(id => id !== fileId);
       }
   } catch (err) {
       console.error(`Error deleting file ${fileId}:`, err);
       if (err.response) { /* ... обработка ошибок 404, 403, 401 ... */ }
       else { error.value = 'Ошибка сети при удалении.'; }
   } finally {
       // Сбросить isDeleting
   }
};
const openPreviewModal = (file) => { /* ... как в MyFilesView ... */
   previewFileDetails.value = { /* ... */ }; showPreviewModal.value = true; };
const closePreviewModal = () => { /* ... как в MyFilesView ... */
  showPreviewModal.value = false; previewFileDetails.value = null; };
const downloadFileFromPreview = (fileId) => { /* ... как в MyFilesView, ищет в filteredFiles ... */
   const file = filteredFiles.value.find(f => f.Id === fileId);
   downloadFile(fileId, file ? file.OriginalName : `file_${fileId}`);
};

// --- Lifecycle ---
onMounted(fetchAllFiles);
onBeforeUnmount(() => { clearTimeout(searchTimeout.value); });

</script>

<style scoped>
  /* Все стили из предыдущей версии AdminFileBrowser */
  .admin-file-browser { padding: 20px; background-color: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08); }
  h2 { margin-top: 0; margin-bottom: 25px; color: #333; border-bottom: 1px solid #eee; padding-bottom: 10px; }

  /* Поиск */
  .search-container { margin-bottom: 20px; position: relative; max-width: 450px; }
  .search-container input[type="search"] { width: 100%; padding: 10px 35px 10px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; font-size: 0.95rem; }
  .clear-search-button { position: absolute; right: 5px; top: 50%; transform: translateY(-50%); background: none; border: none; font-size: 1.6rem; font-weight: bold; cursor: pointer; color: #aaa; padding: 0 5px; line-height: 1; }
  .clear-search-button:hover { color: #555; }

  /* Контролы */
  .controls { margin-bottom: 20px; display: flex; align-items: center; gap: 15px; min-height: 38px; }
  .refresh-button { padding: 10px 18px; background-color: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; transition: background-color 0.2s; }
  .refresh-button:hover:not(:disabled) { background-color: #0056b3; }
  .refresh-button:disabled { background-color: #cccccc; cursor: not-allowed; }
  .loading-indicator.small { display: inline-block; padding: 0; margin: 0; margin-left: 10px; color: #6c757d; font-style: italic; }

  /* Сообщения и индикаторы */
  .loading-indicator, .no-files-message, .error-message { text-align: center; padding: 20px; margin-top: 20px; border-radius: 4px; }
  .loading-indicator { color: #6c757d; }
  .no-files-message { background-color: #e9ecef; color: #495057; }
  .error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
  .file-list-container { min-height: 150px; } /* Предотвращаем скачки высоты */


  /* Группы и список */
  .file-groups-container { margin-top: 20px; }
  .file-group { margin-bottom: 30px; border: 1px solid #dee2e6; border-radius: 6px; background-color: #f8f9fa; }
  /* Стиль для группы результатов поиска */
  .search-results-group { background-color: #e7f3ff; border-color: #bee5eb;}
  .search-results-group h3 { background-color: #cce5ff; color: #004085; border-color: #b8daff; }

  .file-group h3 { background-color: #e9ecef; margin: 0; padding: 12px 15px; font-size: 1.1rem; color: #495057; border-bottom: 1px solid #dee2e6; border-radius: 6px 6px 0 0; }
  .group-name, .user-id { font-weight: bold; color: #343a40; }
  .file-list { list-style: none; padding: 15px; margin: 0; }

  /* Элемент списка */
  .file-item { display: flex; align-items: center; padding: 12px 15px; border: 1px solid #e9ecef; border-radius: 5px; margin-bottom: 10px; background-color: #fff; transition: box-shadow 0.2s ease; }
  .file-item:hover { box-shadow: 0 1px 4px rgba(0,0,0,0.1); }
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