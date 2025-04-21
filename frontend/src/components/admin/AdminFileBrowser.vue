<template>
  <div class="admin-file-browser">
    <h2>Обзор файлов</h2>

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

    <div class="controls">
       <button @click="fetchAllFiles" :disabled="isLoading" class="refresh-button">
         <span v-if="isLoading && !isLoadingSearch">Обновление...</span>
         <span v-else>Обновить список</span>
       </button>
       <span v-if="isLoadingSearch" class="loading-indicator small">Поиск...</span>
    </div>

     <div v-if="error" class="error-message" role="alert">{{ error }}</div>

      <div v-if="!isLoading || groupedAndFilteredFiles.length > 0" class="file-list-container" aria-live="polite">
           <div v-if="isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-group search-results-group">
               <h3>Результаты поиска по запросу: "{{ searchQuery }}" (Найдено: {{ filteredFiles.length }})</h3>
               <ul class="file-list">
                   <li v-for="file in filteredFiles" :key="file.Id" class="file-item">
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

           <div v-else-if="!isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-groups-container">
               <div v-for="group in groupedAndFilteredFiles" :key="group.key" class="file-group">
                 <h3>Группа: <span class="group-name">{{ group.groupName }}</span> | Пользователь ID: <span class="user-id">{{ group.userId }}</span> ({{ group.files.length }} файлов)</h3>
                 <ul class="file-list">
                  <li v-for="file in group.files" :key="file.Id" class="file-item">
                        <div class="file-icon">📄</div>
                         <div class="file-details">
                            <span class="file-name" :title="file.OriginalName">{{ file.OriginalName }}</span>
                            <span class="file-meta">
                              User ID: {{ file.UserId }} | Группа: <strong :title="`Текущая группа: ${file.UserGroup || 'Не назначена'}`">{{ file.UserGroup || 'N/A' }}</strong> | Размер: {{ formatBytes(file.SizeBytes) }} | Загружен: {{ formatDate(file.UploadedAt) }} | Тип: {{ file.ContentType || 'N/A' }}
                            </span>
                        </div>
                         <div class="file-actions">
                             <button v-if="canChangeGroup(file)" @click="openChangeGroupModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button edit-group-button" title="Сменить группу файла" aria-label="Сменить группу файла">✏️</button>
                             <button @click="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр" aria-label="Предпросмотр файла">👁️</button>
                             <button @click="downloadFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать файл" aria-label="Скачать файл"> <span v-if="isDownloading === file.Id">...</span><span v-else>📥</span> </button>
                             <button @click="deleteFile(file.Id, file.OriginalName)" :disabled="isActionInProgress(file.Id)" class="action-button delete-button" title="Удалить файл" aria-label="Удалить файл">🗑️</button>
                         </div>
                   </li>
                 </ul>
               </div>
           </div>

          <div v-else-if="!isLoading && !isLoadingSearch && !error" class="no-files-message">
              <p>{{ isUsingSearchResults ? 'Файлы не найдены по вашему запросу.' : 'Пока не загружено ни одного файла.' }}</p>
          </div>
      </div>
      <div v-else-if="isLoading && !isLoadingSearch" class="loading-indicator">Загрузка списка файлов...</div>


     <FilePreviewModal
         v-if="showPreviewModal"
         :file-id="previewFileDetails.Id"
         :content-type="previewFileDetails.ContentType"
         :original-name="previewFileDetails.OriginalName"
         @close="closePreviewModal"
         @download-original="downloadFileFromPreview"
      />
      <div v-if="showChangeGroupModal" class="modal-overlay" @click.self="closeChangeGroupModal">
            <div class="modal-content change-group-modal">
                 <button @click="closeChangeGroupModal" class="close-button" title="Закрыть">×</button>
                 <h4>Сменить группу для файла: {{ fileToChangeGroup?.OriginalName }}</h4>
                 <p>Текущая группа: <strong>{{ fileToChangeGroup?.UserGroup || 'Не назначена' }}</strong></p>

                 <div class="form-group">
                    <label for="new-group-select">Новая группа:</label>
                    <select id="new-group-select" v-model="selectedNewGroup" :disabled="isUpdatingGroup || availableGroups.length === 0">
                        <option disabled value="">-- Выберите группу --</option>
                        <option v-for="group in assignableGroups" :key="group" :value="group">
                            {{ group }}
                        </option>
                    </select>
                     <small v-if="!isSuperAdmin && assignableGroups.length === 0">Вы не состоите ни в одной группе, куда можно перенести файл.</small>
                     <small v-else-if="availableGroups.length === 0">Нет доступных групп.</small>
                 </div>

                 <div class="modal-actions">
                    <button
                        @click="changeFileGroup"
                        :disabled="isUpdatingGroup || !selectedNewGroup || selectedNewGroup === fileToChangeGroup?.UserGroup"
                        class="save-button">
                        {{ isUpdatingGroup ? 'Сохранение...' : 'Сохранить' }}
                    </button>
                    <button @click="closeChangeGroupModal" :disabled="isUpdatingGroup" class="cancel-button">Отмена</button>
                 </div>
                 <div v-if="changeGroupError" class="error-message small">{{ changeGroupError }}</div>
            </div>
        </div>

  </div>
</template>

<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue';
import axios from 'axios';
import { formatBytes, formatDate } from '@/utils/formatters';
import FilePreviewModal from '@/components/files/FilePreviewModal.vue';

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

const showChangeGroupModal = ref(false);
const fileToChangeGroup = ref(null); // Объект файла { Id, OriginalName, UserGroup, UserId }
const availableGroups = ref([]); // Все группы из AuthService
const selectedNewGroup = ref(''); // Выбранная группа в модалке
const isUpdatingGroup = ref(false);
const changeGroupError = ref('');

const currentUserId = ref(null);
const currentUserRole = ref(null);
const currentUserGroups = ref([]);

const filteredFiles = computed(() => {
  if (!isUsingSearchResults.value) return allFiles.value;
  if (searchResultsById.value.length === 0 && searchQuery.value) return [];
  const searchIdSet = new Set(searchResultsById.value);
  return allFiles.value.filter(file => searchIdSet.has(file.Id));
});

const groupedAndFilteredFiles = computed(() => {
  if (isUsingSearchResults.value) {
      if (!filteredFiles.value || filteredFiles.value.length === 0) return [];
      return [{ key: 'search-results', files: filteredFiles.value }];
  }
  const groups = {};
  allFiles.value.forEach(file => {
    const groupKey = `user-${file.UserId}_group-${file.UserGroup || 'nogroup'}`;
    if (!groups[groupKey]) { groups[groupKey] = { key: groupKey, userId: file.UserId, groupName: file.UserGroup || 'Без группы', files: [] }; }
    groups[groupKey].files.push(file);
  });
  return Object.values(groups).sort((a, b) => {
      if (a.userId !== b.userId) return a.userId - b.userId;
      return (a.groupName || '').localeCompare(b.groupName || '');
  });
});

const isActionInProgress = computed(() => (fileId) => isDownloading.value === fileId || (fileToChangeGroup.value?.Id === fileId && isUpdatingGroup.value)); // Учитываем обновление группы
const isSuperAdmin = computed(() => currentUserRole.value === 'SuperAdmin');

const assignableGroups = computed(() => {
    if (isSuperAdmin.value) {
        return availableGroups.value;
    }
    return availableGroups.value.filter(g => currentUserGroups.value.includes(g));
});

const loadCurrentUser = () => {
     try {
        currentUserId.value = parseInt(localStorage.getItem('userId') || '0');
        currentUserRole.value = localStorage.getItem('userRole');
        currentUserGroups.value = JSON.parse(localStorage.getItem('userGroups') || '[]');
        //console.log('AdminBrowser Current User:', {id: currentUserId.value, role: currentUserRole.value, groups: currentUserGroups.value });
     } catch(e) { console.error("Failed to load current user data:", e); error.value="Ошибка загрузки данных пользователя."; }
};

const fetchAllFiles = async () => {
  if (isLoading.value) return;
  isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
  try {
      const response = await axios.get('/api/file/files');
      allFiles.value = response.data || [];
      //console.log("Fetched all files:", allFiles.value);
  } catch (err) {
      //console.error('Error fetching all files:', err);
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
      const response = await axios.get(`/api/search`, { params: { term: searchQuery.value } });
      searchResultsById.value = response.data || [];
  } catch (err) {
      //console.error('Error searching files:', err);
      searchResultsById.value = [];
      if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
      else { error.value = 'Ошибка при поиске файлов.'; }
  } finally { isLoadingSearch.value = false; }
};

const debouncedSearchFiles = () => { clearTimeout(searchTimeout.value); searchTimeout.value = setTimeout(performSearch, 500); };
const clearSearch = () => { searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false; error.value = ''; };
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
const deleteFile = async (fileId, originalName) => {
   if (!confirm(`Уверены, что хотите удалить файл "${originalName}"?`)) return;
   error.value = '';
   try {
       await axios.delete(`/api/file/files/${fileId}`);
       allFiles.value = allFiles.value.filter(f => f.Id !== fileId);
       if(isUsingSearchResults.value) {
            searchResultsById.value = searchResultsById.value.filter(id => id !== fileId);
       }
   } catch (err) {
       //console.error(`Error deleting file ${fileId}:`, err);
       if (err.response) { /* ... обработка ошибок 404, 403, 401 ... */ }
       else { error.value = 'Ошибка сети при удалении.'; }
   } finally {
       // Сбросить isDeleting
   }
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

const fetchAvailableGroups = async () => {
    console.log("Fetching available groups...");
    try {
        const response = await axios.get('/api/auth/groups');
        availableGroups.value = response.data || [];
        //console.log("Available groups loaded:", availableGroups.value);
    } catch (err) {
        //console.error("Error fetching available groups:", err);
        error.value = "Не удалось загрузить список доступных групп.";
    }
};

const canChangeGroup = (file) => {
    if (!file || !currentUserRole.value) return false;
    if (isSuperAdmin.value) return true;
    if (currentUserRole.value === 'Admin') {
         const isAdminOwner = file.UserId === currentUserId.value;
         const isFileInAdminGroup = file.UserGroup && currentUserGroups.value.includes(file.UserGroup);
         return isAdminOwner || isFileInAdminGroup;
    }
    return false;
};

const openChangeGroupModal = (file) => {
    if (!canChangeGroup(file)) return;
    //console.log("Opening change group modal for:", file);
    fileToChangeGroup.value = { ...file };
    selectedNewGroup.value = '';
    changeGroupError.value = '';
    showChangeGroupModal.value = true;
     if (availableGroups.value.length === 0 && (isSuperAdmin.value || currentUserRole.value === 'Admin')) {
         fetchAvailableGroups();
     }
};

const closeChangeGroupModal = () => {
    showChangeGroupModal.value = false;
    fileToChangeGroup.value = null;
    selectedNewGroup.value = '';
    changeGroupError.value = '';
};

const changeFileGroup = async () => {
    if (!fileToChangeGroup.value || !selectedNewGroup.value || isUpdatingGroup.value || selectedNewGroup.value === fileToChangeGroup.value.UserGroup) {
        return;
    }
    isUpdatingGroup.value = true;
    changeGroupError.value = '';
    const fileId = fileToChangeGroup.value.Id;

    try {
        const payload = { newGroup: selectedNewGroup.value };
        await axios.put(`/api/file/files/${fileId}/group`, payload);

         closeChangeGroupModal();
         await fetchAllFiles();

    } catch (err) {
        //console.error(`Error changing group for file ${fileId}:`, err);
        if (err.response) {
            changeGroupError.value = `Ошибка (${err.response.status}): ${err.response.data?.title || err.response.data || 'Не удалось изменить группу.'}`;
        } else {
            changeGroupError.value = 'Ошибка сети при смене группы.';
        }
    } finally {
        isUpdatingGroup.value = false;
    }
};
onMounted(() => {
    loadCurrentUser();
    fetchAllFiles();
    if (currentUserRole.value === 'SuperAdmin' || currentUserRole.value === 'Admin') {
        fetchAvailableGroups();
    }
});
onBeforeUnmount(() => { clearTimeout(searchTimeout.value); });

</script>

<style scoped>
  .admin-file-browser { padding: 20px; background-color: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08); }
  h2 { margin-top: 0; margin-bottom: 25px; color: #333; border-bottom: 1px solid #eee; padding-bottom: 10px; }

  .search-container { margin-bottom: 20px; position: relative; max-width: 450px; }
  .search-container input[type="search"] { width: 100%; padding: 10px 35px 10px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; font-size: 0.95rem; }
  .clear-search-button { position: absolute; right: 5px; top: 50%; transform: translateY(-50%); background: none; border: none; font-size: 1.6rem; font-weight: bold; cursor: pointer; color: #aaa; padding: 0 5px; line-height: 1; }
  .clear-search-button:hover { color: #555; }

  .controls { margin-bottom: 20px; display: flex; align-items: center; gap: 15px; min-height: 38px; }
  .refresh-button { padding: 10px 18px; background-color: #007bff; color: white; border: none; border-radius: 4px; cursor: pointer; transition: background-color 0.2s; }
  .refresh-button:hover:not(:disabled) { background-color: #0056b3; }
  .refresh-button:disabled { background-color: #cccccc; cursor: not-allowed; }
  .loading-indicator.small { display: inline-block; padding: 0; margin: 0; margin-left: 10px; color: #6c757d; font-style: italic; }

  .loading-indicator, .no-files-message, .error-message { text-align: center; padding: 20px; margin-top: 20px; border-radius: 4px; }
  .loading-indicator { color: #6c757d; }
  .no-files-message { background-color: #e9ecef; color: #495057; }
  .error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
  .file-list-container { min-height: 150px; } /* Предотвращаем скачки высоты */


  .file-groups-container { margin-top: 20px; }
  .file-group { margin-bottom: 30px; border: 1px solid #dee2e6; border-radius: 6px; background-color: #f8f9fa; }
  .search-results-group { background-color: #e7f3ff; border-color: #bee5eb;}
  .search-results-group h3 { background-color: #cce5ff; color: #004085; border-color: #b8daff; }

  .file-group h3 { background-color: #e9ecef; margin: 0; padding: 12px 15px; font-size: 1.1rem; color: #495057; border-bottom: 1px solid #dee2e6; border-radius: 6px 6px 0 0; }
  .group-name, .user-id { font-weight: bold; color: #343a40; }
  .file-list { list-style: none; padding: 15px; margin: 0; }

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
  .edit-group-button { color: #ffc107; }

      .change-group-modal .form-group { margin-bottom: 20px; }
    .change-group-modal label { display: block; margin-bottom: 5px; font-weight: bold; }
    .change-group-modal select { width: 100%; padding: 10px; border: 1px solid #ccc; border-radius: 4px; }
    .change-group-modal small { font-size: 0.85em; color: #6c757d; display: block; margin-top: 5px; }
    .change-group-modal .modal-actions { margin-top: 25px; display: flex; justify-content: flex-end; gap: 10px; }
    .change-group-modal button { padding: 10px 20px; border-radius: 4px; cursor: pointer; border: none; }
    .save-button { background-color: #28a745; color: white; }
    .save-button:disabled { background-color: #ccc; }
    .cancel-button { background-color: #6c757d; color: white; }
    .change-group-modal .error-message.small { margin-top: 15px; text-align: left; padding: 8px 10px; }
</style>