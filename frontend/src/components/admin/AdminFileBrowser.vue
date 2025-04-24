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
             <!-- Результаты поиска -->
             <div v-if="isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-group search-results-group">
                 <h3>Результаты поиска по запросу: "{{ searchQuery }}" (Найдено: {{ filteredFiles.length }})</h3>
                 {{ console.log('Rendering search results, files:', filteredFiles) }}
                 <ul class="file-list">
                    <li v-for="file in filteredFiles" :key="file.Id" class="file-item-wrapper"> 
                        <div style="position:absolute; top:0; left: 0; background: lime; color: black; font-size: 9px; z-index: 100; padding: 1px 3px;">
                         CanChange: {{ canChangeGroup(file) }} | File U:{{ file?.UserId }} G:{{ file?.UserGroup }} | Curr U:{{ currentUserId }} R:{{ currentUserRole }} Gs:[{{ (currentUserGroups || []).join(',') }}] Assignable:[{{ (adminAssignableGroups || []).join(',') }}] HasOther:[{{ (currentUserGroups || []).some(g => g !== file.UserGroup) }}]
                     </div>
                      <div class="file-content-area" @click="openPreviewModal(file)">
                          <FileListItem
                              :file="file"
                              :is-action-in-progress="isActionInProgress(file.Id)"
                              :action-type="getActionType(file.Id)"
                              :show-delete-button="false"  
                              :show-owner-info="true"
                              @download-file="downloadFileFromList"
                              @delete-file="deleteFileFromList"
                              class="file-list-item-component"
                          />
                      </div>
                      <!-- Кнопки действий отдельно -->
                      <div class="file-actions-wrapper">
                           <button v-if="canChangeGroup(file)" @click.stop="openChangeGroupModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button edit-group-button" title="Сменить группу файла">✏️</button>
                           <button @click.stop="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр">👁️</button>
                           <button @click.stop="downloadFileFromList(file.Id)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать"> <span v-if="getActionType(file.Id) === 'download'">...</span><span v-else>📥</span> </button>
                           <button @click.stop="deleteFileFromList(file.Id)" :disabled="isActionInProgress(file.Id)" class="action-button delete-button" title="Удалить"> <span v-if="getActionType(file.Id) === 'delete'">...</span><span v-else>🗑️</span> </button>
                      </div>
                    </li>
                 </ul>
             </div>
  
              <div v-else-if="!isUsingSearchResults && groupedAndFilteredFiles.length > 0" class="file-groups-container">
                  <div v-for="group in groupedAndFilteredFiles" :key="group.key" class="file-group">
                    <h3>Группа: <span class="group-name">{{ group.groupName }}</span> | Пользователь ID: <span class="user-id">{{ group.userId }}</span> ({{ group.files.length }} файлов)</h3>
                    <ul class="file-list">
                      <li v-for="file in group.files" :key="file.Id" class="file-item-wrapper">
                          <div class="file-content-area" @click="openPreviewModal(file)">
                              <FileListItem
                                  :file="file"
                                  :is-action-in-progress="isActionInProgress(file.Id)"
                                  :action-type="getActionType(file.Id)"
                                  :show-delete-button="false"
                                  :show-owner-info="true"
                                  @download-file="downloadFileFromList"
                                  @delete-file="deleteFileFromList"
                                  class="file-list-item-component"
                              />
                          </div>
                          <div class="file-actions-wrapper">
                               <button v-if="canChangeGroup(file)" @click.stop="openChangeGroupModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button edit-group-button" title="Сменить группу файла">✏️</button>
                               <button @click.stop="openPreviewModal(file)" :disabled="isActionInProgress(file.Id)" class="action-button preview-button" title="Предпросмотр">👁️</button>
                               <button @click.stop="downloadFileFromList(file.Id)" :disabled="isActionInProgress(file.Id)" class="action-button download-button" title="Скачать"> <span v-if="getActionType(file.Id) === 'download'">...</span><span v-else>📥</span> </button>
                               <button @click.stop="deleteFileFromList(file.Id)" :disabled="isActionInProgress(file.Id)" class="action-button delete-button" title="Удалить"> <span v-if="getActionType(file.Id) === 'delete'">...</span><span v-else>🗑️</span> </button>
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
           @download-original="downloadFileFromModal"
        />
        <div v-if="showChangeGroupModal" class="modal-overlay" @click.self="closeChangeGroupModal">
              <div class="modal-content change-group-modal">
                   <button @click="closeChangeGroupModal" class="close-button" title="Закрыть">×</button>
                   <h4>Сменить группу для файла: {{ fileToChangeGroup?.OriginalName }}</h4>
                   <p>Текущая группа: <strong>{{ fileToChangeGroup?.UserGroup || 'Не назначена' }}</strong></p>
  
                   <div class="form-group">
                      <label for="new-group-select">Новая группа:</label>
                      <select id="new-group-select" v-model="selectedNewGroup" :disabled="isUpdatingGroup || assignableGroupsForModal.length === 0"> <!-- Проверяем assignableGroupsForModal -->
                          <option disabled value="">-- Выберите группу --</option>
                          <option v-for="group in assignableGroupsForModal" :key="group" :value="group">
                              {{ group }}
                          </option>
                      </select>
                      <small v-if="!isSuperAdmin && assignableGroupsForModal.length === 0 && currentUserGroups.length > 0">Нет подходящих групп для переноса этого файла (вы должны состоять в целевой группе, и она должна отличаться от текущей).</small>
                       <small v-else-if="!isSuperAdmin && currentUserGroups.length === 0">Вы не состоите ни в одной группе, поэтому не можете менять группы файлов.</small>
                       <small v-else-if="availableGroups.length === 0">Нет доступных групп в системе.</small>
                       <small v-if="assignableGroupsForModal.length === 0 && (isSuperAdmin || currentUserGroups.length > 0)">Нет доступных групп для смены.</small> <!-- Общее сообщение -->
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
  import FileListItem from '@/components/files/FileListItem.vue';
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
  const isDeleting = ref(null); // Добавляем флаг удаления
  
  const showChangeGroupModal = ref(false);
  const fileToChangeGroup = ref(null); // Объект файла { Id, OriginalName, UserGroup, UserId }
  const availableGroups = ref([]); // Все группы из AuthService
  const selectedNewGroup = ref(''); // Выбранная группа в модалке
  const isUpdatingGroup = ref(false);
  const changeGroupError = ref('');
  
  const currentUserId = ref(null);
  const currentUserRole = ref(null);
  const currentUserGroups = ref([]);
  
  const isActionInProgress = computed(() => (fileId) =>
      isDownloading.value === fileId ||
      isDeleting.value === fileId || // Учитываем удаление
      (fileToChangeGroup.value?.Id === fileId && isUpdatingGroup.value)
  );
  
  const getActionType = (fileId) => {
    if (isDownloading.value === fileId) return 'download';
    if (isDeleting.value === fileId) return 'delete';
    if (fileToChangeGroup.value?.Id === fileId && isUpdatingGroup.value) return 'update';
    return null;
  };
  
  const filteredFiles = computed(() => {
    if (!isUsingSearchResults.value) return allFiles.value;
    if (searchResultsById.value.length === 0 && searchQuery.value) return [];
    const searchIdSet = new Set(searchResultsById.value);
    // Убедимся, что allFiles содержит файлы перед фильтрацией
    if (!Array.isArray(allFiles.value)) return [];
    return allFiles.value.filter(file => file && typeof file.Id !== 'undefined' && searchIdSet.has(file.Id));
  });
  
  const groupedAndFilteredFiles = computed(() => {
    if (isUsingSearchResults.value) {
        if (!filteredFiles.value || filteredFiles.value.length === 0) return [];
        return [{ key: 'search-results', groupName: 'Результаты поиска', userId: null, files: filteredFiles.value }];
    }
    // Группировка по UserId и UserGroup
    const groups = {};
     if (!Array.isArray(allFiles.value)) return []; // Проверка, что allFiles массив
    allFiles.value.forEach(file => {
      // Проверка на наличие file и file.UserId
      if (file && typeof file.UserId !== 'undefined') {
          const groupKey = `user-${file.UserId}_group-${file.UserGroup || 'nogroup'}`;
          if (!groups[groupKey]) {
              groups[groupKey] = {
                   key: groupKey,
                   userId: file.UserId,
                   groupName: file.UserGroup || 'Без группы',
                   files: []
              };
          }
          groups[groupKey].files.push(file);
      } else {
           console.warn("Skipping invalid file object during grouping:", file);
      }
    });
    // Сортировка групп
    return Object.values(groups).sort((a, b) => {
        if (a.userId !== b.userId) return (a.userId || 0) - (b.userId || 0); // Обработка возможного null/undefined
        return (a.groupName || '').localeCompare(b.groupName || '');
    });
  });
  
  
  const isSuperAdmin = computed(() => currentUserRole.value === 'SuperAdmin');
  
  const assignableGroups = computed(() => {
      if (isSuperAdmin.value) {
          return availableGroups.value;
      }
      // Возвращаем группы, в которых состоит текущий пользователь (Admin)
      const userGroupsSet = new Set(currentUserGroups.value || []);
      return (availableGroups.value || []).filter(g => userGroupsSet.has(g));
  });
  
  
  // Группы, доступные для ВЫБОРА в модальном окне смены группы
  const assignableGroupsForModal = computed(() => {
      if (!fileToChangeGroup.value) return [];
  
      const currentFileGroup = fileToChangeGroup.value.UserGroup;
  
      if (isSuperAdmin.value) {
          // SuperAdmin может выбрать любую группу, кроме текущей (если она есть)
          return availableGroups.value.filter(g => g !== currentFileGroup);
      }
  
      return (currentUserGroups.value || []).filter(g =>
          availableGroups.value.includes(g) && // Группа существует
          g !== currentFileGroup // Не текущая группа файла
      );
  });
  
  const loadCurrentUser = () => {
       try {
          currentUserId.value = parseInt(localStorage.getItem('userId') || '0');
          currentUserRole.value = localStorage.getItem('userRole');
          currentUserGroups.value = JSON.parse(localStorage.getItem('userGroups') || '[]');
          console.log('AdminFileBrowser - Loaded Current User:', { id: currentUserId.value, role: currentUserRole.value, groups: currentUserGroups.value });
       } catch(e) {
           console.error("AdminFileBrowser - Failed to load current user data:", e);
           error.value="Ошибка загрузки данных пользователя.";
           // Сбрасываем значения при ошибке
           currentUserId.value = null;
           currentUserRole.value = null;
           currentUserGroups.value = [];
        }
  };
  
  const fetchAllFiles = async () => {
    if (isLoading.value) return;
    isLoading.value = true; error.value = ''; searchQuery.value = ''; searchResultsById.value = []; isUsingSearchResults.value = false;
    try {
        const params = currentUserRole.value === 'Admin' ? { scope: 'all' } : {};
        const response = await axios.get('/api/file/files', { params });
        allFiles.value = response.data || [];
        console.log("Fetched all files for admin view:", allFiles.value);
    } catch (err) {
        console.error('Error fetching all files:', err);
        allFiles.value = [];
        if (err.response) {
             if (err.response.status === 403) error.value = 'Доступ запрещен.';
             else if (err.response.status === 401) error.value = 'Сессия истекла.';
             else error.value = `Не удалось загрузить список файлов (${err.response.status}).`;
        } else { error.value = 'Ошибка сети.'; }
    } finally { isLoading.value = false; }
  };
  
  const performSearch = async () => {
    if (!searchQuery.value) { searchResultsById.value = []; isUsingSearchResults.value = false; error.value = ''; return; }
    isLoadingSearch.value = true; error.value = ''; isUsingSearchResults.value = true;
    try {
        // Добавляем scope=all для поиска Admin'а, если нужно искать везде
        // SuperAdmin и так ищет везде
        const params = { term: searchQuery.value };
        if (currentUserRole.value === 'Admin') {
            params.scope = 'all';
        }
        const response = await axios.get(`/api/search`, { params });
        searchResultsById.value = response.data || [];
        console.log("Search results (IDs):", searchResultsById.value);
        // Пересчитываем filteredFiles явно, если computed не сработал
        // filteredFiles.value = allFiles.value.filter(file => file && typeof file.Id !== 'undefined' && new Set(searchResultsById.value).has(file.Id));
    } catch (err) {
        console.error('Error searching files:', err);
        searchResultsById.value = [];
        if (err.response && err.response.status === 401) { error.value = 'Сессия истекла.'; }
        else { error.value = 'Ошибка при поиске файлов.'; }
    } finally { isLoadingSearch.value = false; }
  };
  
  const debouncedSearchFiles = () => { clearTimeout(searchTimeout.value); searchTimeout.value = setTimeout(performSearch, 500); };
  
  const clearSearch = () => {
      searchQuery.value = '';
      searchResultsById.value = [];
      isUsingSearchResults.value = false;
      error.value = '';
  };
  
  const downloadFile = async (fileId, originalName) => {
    if (isDownloading.value === fileId) return; // Предотвращаем двойное скачивание
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
        if (err.response) {
             if (err.response.status === 404) downloadError += ' Файл не найден.';
             else if (err.response.status === 403) downloadError += ' Доступ запрещен.';
             else if (err.response.status === 401) downloadError = 'Сессия истекла.';
             else downloadError += ` Ошибка сервера (${err.response.status}).`;
        } else { downloadError += ' Ошибка сети.'; }
        error.value = downloadError;
    } finally {
         isDownloading.value = null;
    }
  };
  
  const deleteFile = async (fileId, originalName) => {
     if (isDeleting.value === fileId) return; // Предотвращаем двойное удаление
     if (!confirm(`Уверены, что хотите удалить файл "${originalName}" (ID: ${fileId})?`)) return;
  
     isDeleting.value = fileId;
     error.value = '';
     try {
         await axios.delete(`/api/file/files/${fileId}`);
         allFiles.value = allFiles.value.filter(f => f.Id !== fileId);
         if (isUsingSearchResults.value) {
             searchResultsById.value = searchResultsById.value.filter(id => id !== fileId);
         }
         console.log(`File ${fileId} deleted successfully.`);
     } catch (err) {
         console.error(`Error deleting file ${fileId}:`, err);
         let deleteError = `Не удалось удалить файл "${originalName || fileId}".`;
        if (err.response) {
             if (err.response.status === 404) deleteError += ' Файл не найден.';
             else if (err.response.status === 403) deleteError += ' У вас нет прав на удаление этого файла.';
             else if (err.response.status === 401) deleteError = 'Сессия истекла.';
             else deleteError += ` Ошибка сервера (${err.response.status}).`;
        } else { deleteError += ' Ошибка сети.'; }
        error.value = deleteError;
     } finally {
         isDeleting.value = null; // Сбрасываем флаг
     }
  };
  
  // Обработчики для FileListItem
  const downloadFileFromList = (fileId) => {
      const file = allFiles.value.find(f => f.Id === fileId); // Ищем во всех файлах
      if (file) {
          downloadFile(fileId, file.OriginalName);
      } else {
           console.error(`FileListItem download request for unknown ID: ${fileId}`);
           error.value = `Не удалось найти информацию для скачивания файла ID: ${fileId}`;
      }
  };
  
  const deleteFileFromList = (fileId) => {
      const file = allFiles.value.find(f => f.Id === fileId); // Ищем во всех файлах
      if (file) {
          deleteFile(fileId, file.OriginalName);
      } else {
           console.error(`FileListItem delete request for unknown ID: ${fileId}`);
           error.value = `Не удалось найти информацию для удаления файла ID: ${fileId}`;
      }
  };
  
  // Функции для модального окна Preview
  const openPreviewModal = (file) => {
       if (!file || !file.Id) {
            console.warn("Attempted to open preview for invalid file:", file);
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
  const downloadFileFromModal = (fileId) => {
       const file = allFiles.value.find(f => f.Id === fileId);
       downloadFile(fileId, file ? file.OriginalName : `файл_${fileId}`);
  };
  
  // ---- Логика смены группы файла ----
  const fetchAvailableGroups = async () => {
      // Загружаем ВСЕ группы, если SuperAdmin, или только СВОИ, если Admin
      // Бэкенд (/api/auth/groups) теперь сам фильтрует для Admin
      console.log("AdminFileBrowser - Fetching available groups...");
      try {
          const response = await axios.get('/api/auth/groups');
          availableGroups.value = response.data || [];
          console.log("AdminFileBrowser - Fetched availableGroups:", availableGroups.value);
      } catch (err) {
          console.error("AdminFileBrowser - Error fetching available groups:", err);
          error.value = "Не удалось загрузить список доступных групп.";
          availableGroups.value = []; // Очищаем при ошибке
      }
  };
  
  const canChangeGroup = (file) => {
      console.log('canChangeGroup called for file:', file?.OriginalName, 'User ID:', file?.UserId, 'Group:', file?.UserGroup); // Лог 1: Входные данные
      if (!file || !currentUserRole.value || !currentUserId.value) {
          console.log('canChangeGroup: Missing file, role, or currentUserId');
          return false;
      }
  
      console.log('Current User Info:', { id: currentUserId.value, role: currentUserRole.value, isSuper: isSuperAdmin.value, groups: currentUserGroups.value }); // Лог 2: Данные текущего пользователя
  
      if (isSuperAdmin.value) {
          console.log('canChangeGroup: Is SuperAdmin, returning true');
          return true;
      }
  
      if (currentUserRole.value === 'Admin') {
           const isAdminOwner = file.UserId === currentUserId.value;
           const isFileInAdminGroup = !!file.UserGroup && (currentUserGroups.value || []).includes(file.UserGroup);
  
           const canAssignToOtherGroup = (currentUserGroups.value || []).some(adminGroup =>
                  availableGroups.value.includes(adminGroup) && // Группа существует
                  adminGroup !== file.UserGroup // Это не текущая группа файла
           );
  
           console.log('canChangeGroup for Admin:', { isAdminOwner, isFileInAdminGroup, canAssignToOtherGroup }); // Лог 3
  
           const result = (isAdminOwner || isFileInAdminGroup) && canAssignToOtherGroup;
           console.log('canChangeGroup final result for Admin:', result); // Лог 4
           return result;
      }
  
      // Для роли User (на всякий случай, хотя это AdminFileBrowser)
      if (currentUserRole.value === 'User') {
          const isOwner = file.UserId === currentUserId.value;
          const hasOtherGroups = (currentUserGroups.value || []).some(g => g !== file.UserGroup);
          console.log('canChangeGroup for User:', { isOwner, hasOtherGroups });
          return isOwner && hasOtherGroups;
      }
  
  
      console.log('canChangeGroup: Unknown role or condition not met, returning false');
      return false; // По умолчанию запрещаем
  };
  
  
  const openChangeGroupModal = (file) => {
      if (!canChangeGroup(file)) { // Проверяем права перед открытием
           console.warn("Attempted to open change group modal without permission for file:", file?.OriginalName);
           return;
       }
      fileToChangeGroup.value = { ...file }; // Копируем данные файла
      selectedNewGroup.value = ''; // Сбрасываем выбор
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
          changeGroupError.value = !selectedNewGroup.value ? 'Группа не выбрана.' : 'Новая группа совпадает с текущей.';
          return;
      }
      isUpdatingGroup.value = true;
      changeGroupError.value = '';
      const fileId = fileToChangeGroup.value.Id;
  
      try {
          const payload = { newGroup: selectedNewGroup.value };
          await axios.put(`/api/file/files/${fileId}/group`, payload);
  
           closeChangeGroupModal();
           await fetchAllFiles(); // Перезагружаем список файлов после смены группы
  
      } catch (err) {
          console.error(`Error changing group for file ${fileId}:`, err);
          if (err.response) {
              changeGroupError.value = `Ошибка (${err.response.status}): ${err.response.data?.title || err.response.data?.message || err.response.data || 'Не удалось изменить группу.'}`;
          } else {
              changeGroupError.value = 'Ошибка сети при смене группы.';
          }
      } finally {
          isUpdatingGroup.value = false;
      }
  };
  
  onMounted(() => {
      loadCurrentUser();
      if (currentUserId.value) {
          fetchAllFiles();
           // Загружаем группы только если это Admin или SuperAdmin
           if (currentUserRole.value === 'SuperAdmin' || currentUserRole.value === 'Admin') {
               fetchAvailableGroups();
           }
      } else {
           error.value = "Не удалось загрузить данные пользователя. Функционал ограничен.";
      }
  });
  
  onBeforeUnmount(() => {
      clearTimeout(searchTimeout.value);
  });
  
  </script>
  
  <style scoped>
  /* Стили обертки и кнопок */
  .file-list {
      list-style: none;
      padding: 0;
      margin: 0;
  }
  
  .file-item-wrapper {
      display: flex;
      align-items: center;
      border: 1px solid #e9ecef;
      border-radius: 5px;
      margin-bottom: 10px;
      background-color: #fff;
      transition: box-shadow 0.2s ease;
  }
  .file-item-wrapper:hover {
       box-shadow: 0 1px 4px rgba(0,0,0,0.1);
  }
  
  .file-content-area {
      flex-grow: 1;
      cursor: pointer;
  }
  
  .file-list-item-component {
      border: none !important;
      margin-bottom: 0 !important;
      background-color: transparent !important;
      box-shadow: none !important;
      padding: 12px 0 12px 15px !important;
  }
  :deep(.file-list-item-component .file-actions) {
      display: none !important;
  }
  
  
  .file-actions-wrapper {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 0 15px;
      margin-left: auto;
  }
  
  /* Общие стили кнопок */
  .action-button {
      background: none; border: none; padding: 5px; cursor: pointer; font-size: 1.2rem;
      border-radius: 4px; line-height: 1; transition: transform 0.1s ease, color 0.2s ease;
      min-width: 30px; min-height: 30px; display: inline-flex; align-items: center; justify-content: center;
  }
  .action-button:hover:not(:disabled) { transform: scale(1.1); }
  .action-button:disabled { opacity: 0.5; cursor: not-allowed; transform: none; }
  .preview-button { color: #6f42c1; }
  .download-button { color: #007bff; }
  .delete-button { color: #dc3545; }
  .edit-group-button { color: #ffc107; }
  
  /* Остальные стили компонента */
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
  .file-list-container { min-height: 150px; }
  .file-groups-container { margin-top: 20px; }
  .file-group { margin-bottom: 30px; border: 1px solid #dee2e6; border-radius: 6px; background-color: #f8f9fa; }
  .search-results-group { background-color: #e7f3ff; border-color: #bee5eb;}
  .search-results-group h3 { background-color: #cce5ff; color: #004085; border-color: #b8daff; }
  .file-group h3 { background-color: #e9ecef; margin: 0; padding: 12px 15px; font-size: 1.1rem; color: #495057; border-bottom: 1px solid #dee2e6; border-radius: 6px 6px 0 0; }
  .group-name, .user-id { font-weight: bold; color: #343a40; }
  
  /* Стили модального окна смены группы */
  .modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background-color: rgba(0, 0, 0, 0.6); display: flex; justify-content: center; align-items: center; z-index: 1000; }
  .modal-content { background-color: white; padding: 30px; border-radius: 8px; min-width: 400px; max-width: 500px; box-shadow: 0 5px 15px rgba(0,0,0,0.3); position: relative; } /* Добавлено position: relative для кнопки закрытия */
  .change-group-modal .close-button { position: absolute; top: 10px; right: 15px; background: none; border: none; font-size: 1.8rem; cursor: pointer; color: #888; padding: 0; line-height: 1; z-index: 10; }
  .change-group-modal .close-button:hover { color: #333; }
  .change-group-modal h4 { margin-top: 0; margin-bottom: 15px; }
  .change-group-modal p { margin-bottom: 20px; }
  .change-group-modal .form-group { margin-bottom: 20px; }
  .change-group-modal label { display: block; margin-bottom: 5px; font-weight: bold; }
  .change-group-modal select { width: 100%; padding: 10px; border: 1px solid #ccc; border-radius: 4px; }
  .change-group-modal select:disabled { background-color: #e9ecef; cursor: not-allowed; }
  .change-group-modal small { font-size: 0.85em; color: #6c757d; display: block; margin-top: 5px; }
  .change-group-modal .modal-actions { margin-top: 25px; display: flex; justify-content: flex-end; gap: 10px; }
  .change-group-modal button { padding: 10px 20px; border-radius: 4px; cursor: pointer; border: none; }
  .save-button { background-color: #28a745; color: white; }
  .save-button:disabled { background-color: #ccc; }
  .cancel-button { background-color: #6c757d; color: white; }
  .change-group-modal .error-message.small { margin-top: 15px; text-align: left; padding: 8px 10px; background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; border-radius: 4px; }
  </style>