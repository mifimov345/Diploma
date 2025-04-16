<template>
    <div class="my-files-view">
      <h2>Мои файлы</h2>
  
      <div class="controls">
         <button @click="fetchMyFiles" :disabled="isLoading" class="refresh-button">
          <span v-if="isLoading">Обновление...</span>
          <span v-else>Обновить список</span>
        </button>
      </div>
  
      <div v-if="isLoading && files.length === 0" class="loading-indicator">
        Загрузка файлов...
      </div>
  
      <div v-if="error" class="error-message">
        {{ error }}
      </div>
  
      <div v-if="!isLoading && files.length === 0 && !error" class="no-files-message">
        У вас пока нет загруженных файлов. Вы можете загрузить их на странице "Загрузить файл".
      </div>
  
      <ul v-if="files.length > 0" class="file-list">
        <li v-for="file in files" :key="file.Id" class="file-item">
          <div class="file-icon">
            📄
          </div>
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
          </div>
        </li>
      </ul>
    </div>
  </template>
  
  <script>
  import axios from 'axios';
  import { formatBytes, formatDate } from '@/utils/formatters';
  
  export default {
    name: 'MyFilesView',
    data() {
      return {
        files: [],
        isLoading: false,
        isDownloading: null,
        error: '',
      };
    },
    methods: {
      formatBytes,
      formatDate,
  
      async fetchMyFiles() {
        if (this.isLoading) return;
        this.isLoading = true;
        this.error = '';
  
        try {
          const response = await axios.get('/api/file/files');
          this.files = response.data;
          if (this.files.length === 0) {
          }
        } catch (err) {
          console.error('Error fetching my files:', err);
          this.files = [];
          if (err.response && err.response.status === 401) {
              this.error = 'Сессия истекла или недействительна. Пожалуйста, войдите снова.';
          } else {
              this.error = 'Не удалось загрузить список файлов. Попробуйте обновить страницу.';
          }
        } finally {
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
                  downloadError = `Файл "${originalName}" не найден на сервере. Возможно, он был удален.`;
                  this.fetchMyFiles();
               } else if (err.response.status === 403) {
                   downloadError = `У вас нет прав на скачивание файла "${originalName}".`;
               } else if (err.response.status === 401) {
                   downloadError = 'Ошибка авторизации при скачивании. Пожалуйста, войдите снова.';
               }
           }
           this.error = downloadError;
        } finally {
          this.isDownloading = null;
        }
      },
    },
    mounted() {
      this.fetchMyFiles();
    },
  };
  </script>
  
  <style scoped>
  .my-files-view {
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
  
  .controls {
    margin-bottom: 20px;
  }
  
  .refresh-button {
    padding: 10px 18px;
    background-color: #17a2b8;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.2s;
  }
  .refresh-button:hover:not(:disabled) {
    background-color: #138496;
  }
  .refresh-button:disabled {
      background-color: #cccccc;
      cursor: not-allowed;
  }
  
  .loading-indicator, .no-files-message, .error-message {
    text-align: center;
    padding: 20px;
    margin-top: 20px;
    border-radius: 4px;
  }
  
  .loading-indicator {
    color: #6c757d;
  }
  .no-files-message {
    background-color: #e9ecef;
    color: #495057;
  }
  .error-message {
    background-color: #f8d7da;
    color: #721c24;
    border: 1px solid #f5c6cb;
  }
  
  .file-list {
    list-style: none;
    padding: 0;
    margin: 0;
  }
  
  .file-item {
    display: flex;
    align-items: center;
    padding: 15px;
    border: 1px solid #e9ecef;
    border-radius: 5px;
    margin-bottom: 10px;
    background-color: #fff;
    transition: box-shadow 0.2s ease;
  }
  .file-item:hover {
      box-shadow: 0 2px 5px rgba(0,0,0,0.1);
  }
  
  .file-icon {
    font-size: 1.8rem;
    margin-right: 15px;
    color: #6c757d;
  }
  
  .file-details {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
  }
  
  .file-name {
    font-weight: bold;
    color: #343a40;
    margin-bottom: 3px;
    word-break: break-all;
  }
  
  .file-meta {
    font-size: 0.85rem;
    color: #6c757d;
  }
  
  .file-actions {
    margin-left: 20px;
    display: flex;
    gap: 10px;
  }
  
  .action-button {
    background: none;
    border: none;
    padding: 5px;
    cursor: pointer;
    font-size: 1.2rem;
    transition: transform 0.2s ease;
  }
  .action-button:hover:not(:disabled) {
      transform: scale(1.1);
  }
  .action-button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
  }
  
  .download-button { color: #007bff; }
  .delete-button { color: #dc3545; }
  
  </style>