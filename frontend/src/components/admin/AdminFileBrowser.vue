<template>
    <div class="admin-file-browser">
      <h2>Все файлы пользователей</h2>
  
      <div class="controls">
        <button @click="fetchAllFiles" :disabled="isLoading" class="refresh-button">
          <span v-if="isLoading">Обновление...</span>
          <span v-else>Обновить список</span>
        </button>
        <!-- Сюда можно добавить фильтры или поиск -->
      </div>
  
      <div v-if="isLoading && groupedFiles.length === 0" class="loading-indicator">
        Загрузка списка файлов...
      </div>
  
      <div v-if="error" class="error-message">
        {{ error }}
      </div>
  
      <div v-if="!isLoading && groupedFiles.length === 0 && !error" class="no-files-message">
        Пока не загружено ни одного файла.
      </div>
  
      <div v-if="groupedFiles.length > 0" class="file-groups-container">
        <div v-for="group in groupedFiles" :key="group.key" class="file-group">
          <h3>
            Группа: <span class="group-name">{{ group.groupName }}</span> | Пользователь ID: <span class="user-id">{{ group.userId }}</span>
          </h3>
          <ul class="file-list">
            <li v-for="file in group.files" :key="file.Id" class="file-item">
               <div class="file-icon">📄</div>
               <div class="file-details">
                  <span class="file-name">{{ file.OriginalName }}</span>
                  <span class="file-meta">
                     Размер: {{ formatBytes(file.SizeBytes) }} | Загружен: {{ formatDate(file.UploadedAt) }}
                  </span>
              </div>
              <div class="file-actions">
                  <button
                    @click="downloadFile(file.Id, file.OriginalName)"
                    :disabled="isDownloading === file.Id"
                    class="action-button download-button"
                    title="Скачать файл"
                  >
                    <span v-if="isDownloading === file.Id">...</span>
                    <span v-else>📥</span>
                  </button>
                  <button
                      @click="deleteFile(file.Id, file.OriginalName)"
                      class="action-button delete-button"
                      title="Удалить файл"
                      >
                      🗑️
                  </button>
              </div>
            </li>
          </ul>
        </div>
      </div>
    </div>
  </template>
  
  <script>
  import axios from 'axios';
  
  export default {
    name: 'AdminFileBrowser',
    data() {
      return {
        allFiles: [],
        isLoading: false,
        isDownloading: null,
        error: '',
      };
    },
    computed: {
      groupedFiles() {
        console.log("groupedFiles computed - CALLED. this.allFiles:", JSON.stringify(this.allFiles)); // Лог 1
        if (!Array.isArray(this.allFiles)) {
            console.warn("groupedFiles computed: this.allFiles is NOT an array. Returning [].");
            return [];
        }
        if (this.allFiles.length === 0) {
             console.log("groupedFiles computed: this.allFiles is EMPTY array. Returning [].");
             return [];
        }

        const groups = {};
        try {
            this.allFiles.forEach((file, index) => {
                 if (!file || typeof file.UserId === 'undefined' || typeof file.UserGroup === 'undefined') {
                     console.warn(`groupedFiles computed: Invalid file object at index ${index}:`, file);
                     return;
                 }
                const groupKey = `user-${file.UserId}_group-${file.UserGroup}`;
                if (!groups[groupKey]) {
                    groups[groupKey] = {
                        key: groupKey,
                        userId: file.UserId,
                        groupName: file.UserGroup || 'Без группы',
                        files: [],
                    };
                }
                groups[groupKey].files.push(file);
            });
        } catch (e) {
             console.error("Error during file grouping:", e, "this.allFiles was:", this.allFiles);
             return [];
        }

        console.log("groupedFiles computed - Grouping DONE. Groups object:", groups);

        const sorted = Object.values(groups).sort((a, b) => {
             if (a.userId !== b.userId) return a.userId - b.userId;
             return (a.groupName || '').localeCompare(b.groupName || '');
        });
        console.log("groupedFiles computed - RETURNING sorted array (count):", sorted.length);
        return sorted;
    },
},
methods: {
    async fetchAllFiles() {
        console.log("fetchAllFiles - CALLED");
        if (this.isLoading) return;
        this.isLoading = true;
        this.error = '';

        try {
            const response = await axios.get('/api/file/files');
            console.log("fetchAllFiles - RAW RESPONSE:", response);

            if (response && response.data && Array.isArray(response.data)) {
                console.log("fetchAllFiles - Assigning data (count):", response.data.length);
                this.allFiles = response.data;
            } else {
                console.error("fetchAllFiles - Error: response.data is not an array. Received:", response.data);
                this.allFiles = [];
                this.error = 'Получены некорректные данные списка файлов от сервера.';
            }
        } catch (err) {
            console.error('fetchAllFiles - CATCH block:', err);
            this.allFiles = [];
            this.error = err.response?.data?.message || err.message || 'Не удалось загрузить список файлов.';
             if (err.response?.status === 403) {
                 this.error = 'Доступ запрещен. У вас нет прав для просмотра этого раздела.';
             }
        } finally {
            console.log("fetchAllFiles - FINALLY block");
            this.isLoading = false;
        }
    },
  
      async downloadFile(fileId, originalName) {
         if (this.isDownloading) return;
         this.isDownloading = fileId;
         this.error = '';
  
        try {
          const response = await axios.get(`/api/file/download/${fileId}`, {
            responseType: 'blob',
          });
          const url = window.URL.createObjectURL(new Blob([response.data]));
          const link = document.createElement('a');
          link.href = url;
          link.setAttribute('download', originalName || `file_${fileId}`);
          document.body.appendChild(link);
          link.click();
          window.URL.revokeObjectURL(url);
          link.remove();
        } catch (err) {
          console.error(`Error downloading file ${fileId}:`, err);
          let downloadError = `Не удалось скачать файл "${originalName}".`;
           if (err.response) {
               if (err.response.status === 404) {
                  downloadError = `Файл "${originalName}" не найден на сервере.`;
                  this.fetchAllFiles();
               } else if (err.response.status === 401) {
                   downloadError = 'Ошибка авторизации при скачивании. Пожалуйста, войдите снова.';
               }
           }
          this.error = downloadError;
        } finally {
          this.isDownloading = null;
        }
      },
      async deleteFile(fileId, originalName) {
         if (!confirm(`Вы уверены, что хотите удалить файл "${originalName}" (ID: ${fileId})?`)) return;
         this.error = '';

         try {
             await axios.delete(`/api/file/files/${fileId}`);
             this.allFiles = this.allFiles.filter(file => file.Id !== fileId);
         } catch (err) {
              console.error(`Error deleting file ${fileId}:`, err);
              let deleteError = `Не удалось удалить файл "${originalName}".`;
              if (err.response) {
                  if (err.response.status === 404) {
                      deleteError = `Файл "${originalName}" не найден. Возможно, уже удален.`;
                      this.fetchAllFiles();
                  } else if (err.response.status === 403) {
                      deleteError = `У вас нет прав на удаление файла "${originalName}".`;
                  } else if (err.response.status === 401) {
                      deleteError = 'Ошибка авторизации при удалении. Пожалуйста, войдите снова.';
                  }
              }
              this.error = deleteError;
         } finally {
         }
     },
    },
    mounted() {
    console.log("AdminFileBrowser MOUNTED - Calling fetchAllFiles");
    this.fetchAllFiles();
    },
  };
  </script>
  
  <style scoped>
  .admin-file-browser {
    padding: 20px;
    background-color: #fff;
    border-radius: 8px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  }
  
  h2 {
    margin-top: 0;
    margin-bottom: 25px;
    color: #333;
    border-bottom: 1px solid #eee;
    padding-bottom: 10px;
  }
  
  .controls { margin-bottom: 20px; }
  .refresh-button {
    padding: 10px 18px;
    background-color: #007bff;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.2s;
  }
  .refresh-button:hover:not(:disabled) { background-color: #0056b3; }
  .refresh-button:disabled { background-color: #cccccc; cursor: not-allowed; }
  
  .loading-indicator, .no-files-message, .error-message {
    text-align: center;
    padding: 20px;
    margin-top: 20px;
    border-radius: 4px;
  }
  .loading-indicator { color: #6c757d; }
  .no-files-message { background-color: #e9ecef; color: #495057; }
  .error-message { background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
  
  .file-groups-container { margin-top: 20px; }
  
  .file-group {
    margin-bottom: 30px;
    border: 1px solid #dee2e6;
    border-radius: 6px;
    background-color: #f8f9fa;
  }
  
  .file-group h3 {
    background-color: #e9ecef;
    margin: 0;
    padding: 12px 15px;
    font-size: 1.1rem;
    color: #495057;
    border-bottom: 1px solid #dee2e6;
    border-radius: 6px 6px 0 0;
  }
  .group-name, .user-id {
      font-weight: bold;
      color: #343a40;
  }
  
  .file-list {
    list-style: none;
    padding: 15px;
    margin: 0;
  }
  
  .file-item {
    display: flex;
    align-items: center;
    padding: 12px 15px;
    border: 1px solid #e9ecef;
    border-radius: 5px;
    margin-bottom: 10px;
    background-color: #fff;
    transition: box-shadow 0.2s ease;
  }
  .file-item:hover { box-shadow: 0 1px 4px rgba(0,0,0,0.1); }
  
  .file-icon { font-size: 1.8rem; margin-right: 15px; color: #6c757d; }
  .file-details { flex-grow: 1; display: flex; flex-direction: column; }
  .file-name { font-weight: bold; color: #343a40; margin-bottom: 3px; word-break: break-all; }
  .file-meta { font-size: 0.85rem; color: #6c757d; }
  .file-actions { margin-left: 20px; display: flex; gap: 10px; }
  .action-button { background: none; border: none; padding: 5px; cursor: pointer; font-size: 1.2rem; transition: transform 0.2s ease; }
  .action-button:hover:not(:disabled) { transform: scale(1.1); }
  .action-button:disabled { opacity: 0.5; cursor: not-allowed; }
  .download-button { color: #007bff; }
  .delete-button { color: #dc3545; }
  </style>