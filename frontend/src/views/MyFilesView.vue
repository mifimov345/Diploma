<template>
  <div class="my-files-view">
    <h2>Мои файлы</h2>

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

    <div class="controls">
       <button @click="fetchMyFiles" :disabled="isLoading" class="refresh-button">
        <span v-if="isLoading && !isLoadingSearch">Обновление...</span>
        <span v-else>Обновить список</span>
      </button>
       <span v-if="isLoadingSearch" class="loading-indicator small">Поиск...</span>
    </div>

    <div v-if="error" class="error-message" role="alert"> {{ error }} </div>

    <div v-if="!isLoading || filteredFiles.length > 0" class="file-list-container" aria-live="polite">
        <ul v-if="filteredFiles.length > 0" class="file-list no-bullets"> <!-- Добавлен класс no-bullets -->
            <FileListItem
                v-for="file in filteredFiles"
                :key="file.Id"
                :file="file"
                :is-action-in-progress="isActionInProgress(file.Id)"
                :action-type="getActionType(file.Id)"
                :show-delete-button="true"  
                @download-file="downloadFileFromList"
                @delete-file="deleteFileFromList"
                @click="openPreviewModal(file)" 
                class="clickable-list-item" 
            >
              <template #actions>                  
                  <button
                                v-if="canChangeGroup(file)"  
                                @click.stop="openChangeGroupModal(file)"
                                :disabled="isActionInProgress(file.Id)"
                                class="action-button edit-group-button"
                                title="Сменить группу файла"
                                aria-label="Сменить группу файла">
                                ✏️
                           </button>
                           <button
                                @click.stop="openPreviewModal(file)"
                                :disabled="isActionInProgress(file.Id)"
                                class="action-button preview-button"
                                title="Предпросмотр"
                                aria-label="Предпросмотр файла">
                               👁️
                          </button>
              </template>
             <template #meta>
                  | Группа: <strong :title="`Текущая группа: ${file.UserGroup || 'Не назначена'}`">{{ file.UserGroup || 'N/A' }}</strong>
                  | Тип: {{ file.ContentType || 'N/A' }}
             </template>
           </FileListItem>
      </ul>
        <div v-else-if="!isLoading && !isLoadingSearch && !error" class="no-files-message">
            <p>{{ isUsingSearchResults ? 'Файлы не найдены по вашему запросу.' : 'У вас пока нет загруженных файлов.' }}</p>
        </div>
    </div>
     <div v-else-if="isLoading && !isLoadingSearch" class="loading-indicator"> Загрузка файлов... </div>

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
                 <label for="new-group-select-user">Новая группа:</label>
                 <select id="new-group-select-user" v-model="selectedNewGroup" :disabled="isUpdatingGroup || currentUserGroups.length === 0">
                     <option disabled value="">-- Выберите группу --</option>
                     <option v-for="group in currentUserGroups" :key="group" :value="group">
                         {{ group }}
                     </option>
                 </select>
                 <small v-if="currentUserGroups.length === 0">Вы не состоите ни в одной группе, поэтому не можете сменить группу файла.</small>
                </div>
              <div class="modal-actions">
                 <button @click="changeFileGroup" :disabled="isUpdatingGroup || !selectedNewGroup || selectedNewGroup === fileToChangeGroup?.UserGroup" class="save-button"> {{ isUpdatingGroup ? 'Сохранение...' : 'Сохранить' }} </button>
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
import FilePreviewModal from '@/components/files/FilePreviewModal.vue';
import FileListItem from '@/components/files/FileListItem.vue';

const isDeleting = ref(null); // Флаг для удаления
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

const showChangeGroupModal = ref(false);
const fileToChangeGroup = ref(null);
const selectedNewGroup = ref('');
const isUpdatingGroup = ref(false);
const changeGroupError = ref('');
const currentUserGroups = ref([]);

const getActionType = (fileId) => {
  if (isDownloading.value === fileId) return 'download';
  if (isDeleting.value === fileId) return 'delete';
  return null;
};

// MyFilesView.vue -> canChangeGroup
const canChangeGroup = (file) => {
    console.log('[MyFiles] canChangeGroup called for file:', file?.OriginalName, 'FileUserId:', file?.UserId, 'FileGroup:', file?.UserGroup);
    if (!file || !currentUserId.value) {
        console.log('[MyFiles] canChangeGroup: Missing file or currentUserId.value');
        return false;
    }

    console.log('[MyFiles] Current User Data:', { id: currentUserId.value, groups: currentUserGroups.value });

    const isOwner = file.UserId === currentUserId.value;
    console.log('[MyFiles] isOwner:', isOwner);

    if (!isOwner) {
         console.log('[MyFiles] canChangeGroup: Not the owner, returning false.');
         return false;
    }

    const userHasAnyGroups = Array.isArray(currentUserGroups.value) && currentUserGroups.value.length > 0;
    console.log('[MyFiles] userHasAnyGroups:', userHasAnyGroups);

    if (!userHasAnyGroups) {
        console.log('[MyFiles] canChangeGroup: User has no groups, returning false.');
        return false;
    }

    const isFileWithoutGroup = !file.UserGroup;
    const hasOtherGroups = currentUserGroups.value.some(g => g !== file.UserGroup);
    console.log('[MyFiles] isFileWithoutGroup:', isFileWithoutGroup);
    console.log('[MyFiles] hasOtherGroups (different from file group):', hasOtherGroups);

    const result = isFileWithoutGroup || hasOtherGroups; // Условие изменено для ясности
    console.log('[MyFiles] canChangeGroup final result:', result);

    return result;
};

const downloadFileFromList = (fileId) => {
    const file = filteredFiles.value.find(f => f.Id === fileId);
    if (file) {
        downloadFile(fileId, file.OriginalName);
    }
};

const deleteFileFromList = (fileId) => {
    const file = filteredFiles.value.find(f => f.Id === fileId);
    if (file) {
        deleteFile(fileId, file.OriginalName);
    }
};

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

const isActionInProgress = computed(() => (fileId) => isDownloading.value === fileId || isDeleting.value === fileId || (fileToChangeGroup.value?.Id === fileId && isUpdatingGroup.value));

const loadCurrentUser = () => {
     try {
        currentUserId.value = parseInt(localStorage.getItem('userId') || '0');
        currentUserGroups.value = JSON.parse(localStorage.getItem('userGroups') || '[]');
        //console.log('MyFilesView Current User:', {id: currentUserId.value, groups: currentUserGroups.value });
        if (isNaN(currentUserId.value) || currentUserId.value <= 0) { throw new Error("Invalid userId"); }
     } catch (e) {
          //console.error("MyFilesView: Error loading current user data.", e);
          error.value = "Ошибка загрузки данных пользователя.";
     }
};

const fetchMyFiles = async () => {
    if (isLoading.value) return;
    isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
    try {
        const response = await axios.get('/api/file/files');
        files.value = response.data || [];
    } catch (err) {
        //console.error('Error fetching my files:', err);
        files.value = []; // Очищаем при ошибке
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else if (err.response && err.response.status === 403) { error.value = 'Доступ запрещен.'; }
        else { error.value = 'Не удалось загрузить список файлов.'; }
    } finally { isLoading.value = false; }
};

const performSearch = async () => {
    if (!searchQuery.value) {
        searchResultsById.value = [];
        isUsingSearchResults.value = false;
        error.value = '';
        return;
    }
    if (!currentUserId.value) { 
         error.value = "Не удалось определить ID пользователя для поиска.";
         return;
    }
    isLoadingSearch.value = true; error.value = ''; isUsingSearchResults.value = true;
    try {
        const response = await axios.get(`/api/search`, { params: { term: searchQuery.value, userId: currentUserId.value } });
        searchResultsById.value = response.data || [];
    } catch (err) {
        //console.error('Error searching files:', err);
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

const deleteFile = async (fileId, originalName) => {
  if (isDeleting.value) return; // Не удалять, если уже идет удаление
  if (!confirm(`Уверены, что хотите удалить файл "${originalName}"? Это действие необратимо.`)) {
      return;
  }

  isDeleting.value = fileId; // Устанавливаем флаг удаления
  error.value = '';

  try {
      await axios.delete(`/api/file/files/${fileId}`);

      // Удаляем файл из локального списка
      files.value = files.value.filter(f => f.Id !== fileId);
      // Если были результаты поиска, удаляем и оттуда
      if (isUsingSearchResults.value) {
          searchResultsById.value = searchResultsById.value.filter(id => id !== fileId);
      }
      // Можно добавить сообщение об успехе, если нужно

  } catch (err) {
      console.error(`Error deleting file ${fileId}:`, err);
      let deleteError = `Не удалось удалить файл "${originalName || fileId}".`;
      if (err.response) {
          if (err.response.status === 404) deleteError += ' Файл не найден.';
          else if (err.response.status === 403) deleteError += ' У вас нет прав на удаление этого файла.';
          else if (err.response.status === 401) deleteError = 'Сессия истекла.';
          else deleteError += ` Ошибка сервера (${err.response.status}).`;
      } else {
          deleteError += ' Ошибка сети.';
      }
      error.value = deleteError;
  } finally {
      isDeleting.value = null; // Сбрасываем флаг удаления
  }
};

const openPreviewModal = (file) => {
    //onsole.log('Opening preview. File object:', file);
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

const openChangeGroupModal = (file) => {
    if (file.UserId !== currentUserId.value) {
         alert("Вы можете изменять группу только для своих файлов.");
         return;
     }
     fileToChangeGroup.value = { ...file };
     selectedNewGroup.value = file.UserGroup || '';
     changeGroupError.value = '';
     showChangeGroupModal.value = true;
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

    if (!currentUserGroups.value.includes(selectedNewGroup.value)) {
        changeGroupError.value = "Вы не состоите в выбранной группе.";
        return;
    }
    isUpdatingGroup.value = true;
    changeGroupError.value = '';
    const fileId = fileToChangeGroup.value.Id;

    try {
        const payload = { newGroup: selectedNewGroup.value };
        await axios.put(`/api/file/files/${fileId}/group`, payload);

        const fileIndex = files.value.findIndex(f => f.Id === fileId);
        if (fileIndex > -1) {
            files.value[fileIndex].UserGroup = selectedNewGroup.value;
        }

        closeChangeGroupModal();

    } catch (err) {
        console.error(`Error changing group for file ${fileId}:`, err);
        if (err.response) {
            if (err.response.status === 403) { changeGroupError.value = 'Недостаточно прав для изменения группы.'; }
            else if (err.response.status === 404) { changeGroupError.value = 'Файл не найден.'; }
            else { changeGroupError.value = `Ошибка (${err.response.status}): ${err.response.data?.title || err.response.data || 'Не удалось изменить группу.'}`; }
        } else { changeGroupError.value = 'Ошибка сети.'; }
    } finally {
        isUpdatingGroup.value = false;
    }
};

// --- Lifecycle ---
onMounted(() => {
    loadCurrentUser();
    if (currentUserId.value) { fetchMyFiles(); }
    else { error.value = "Не удалось загрузить ID пользователя. Функционал ограничен."; }
 });

onBeforeUnmount(() => {
    clearTimeout(searchTimeout.value);
});

</script>

<style scoped>

.file-list.no-bullets {
  list-style-type: none;
  padding-left: 0;
}

.clickable-list-item {
    cursor: pointer;
}

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
.delete-button { color: #dc3545; }
.edit-group-button { color: #ffc107; }

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

.edit-group-button { color: #ffc107; } /* Оранжевый */
.file-meta strong { font-weight: 600; color: #555; } /* Выделяем текущую группу */
.change-group-modal .error-message.small { margin-top: 15px; text-align: left; padding: 8px 10px; }

</style>