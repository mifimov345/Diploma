<template>
    <li class="file-list-item">
      <div class="file-icon">
        <span :title="file.ContentType || 'Файл'">{{ getFileIcon(file.ContentType) }}</span>
      </div>
      <div class="file-details">
        <span class="file-name" :title="file.OriginalName">{{ file.OriginalName }}</span>
        <span class="file-meta">
          <span title="Размер файла">{{ formatBytes(file.SizeBytes) }}</span> |
          <span title="Дата загрузки">{{ formatDate(file.UploadedAt) }}</span>
          <template v-if="showOwnerInfo && file.UserId">
             | <span title="ID Пользователя">👤 {{ file.UserId }}</span>
             | <span title="Группа пользователя">📁 {{ file.UserGroup || 'N/A' }}</span>
          </template>
        </span>
      </div>
      <div class="file-actions">
        <button
          @click.stop="onDownloadClick"
          :disabled="isActionInProgress"
          class="action-button download-button"
          title="Скачать файл"
        >
          <span v-if="isActionInProgress && actionType === 'download'">...</span>
          <span v-else>📥</span>
        </button>
        <button
          v-if="showDeleteButton"
          @click.stop="onDeleteClick"
          :disabled="isActionInProgress"
          class="action-button delete-button"
          title="Удалить файл"
        >
           <span v-if="isActionInProgress && actionType === 'delete'">...</span>
           <span v-else>🗑️</span>
        </button>
      </div>
    </li>
  </template>
  
  <script>
  import { formatBytes, formatDate } from '@/utils/formatters';
  
  export default {
    name: 'FileListItem',
    props: {
      file: {
        type: Object,
        required: true,
        validator: (value) => {
           return value && typeof value.Id !== 'undefined' && typeof value.OriginalName !== 'undefined';
        }
      },
      isActionInProgress: {
          type: Boolean,
          default: false
      },
      actionType: {
          type: String,
          default: null
      },
      showDeleteButton: {
          type: Boolean,
          default: false
      },
      showOwnerInfo: {
          type: Boolean,
          default: false
      }
    },
    emits: ['download-file', 'delete-file'],
    methods: {
      formatBytes,
      formatDate,
  
      getFileIcon(contentType) {
        if (!contentType) return '❓';
        if (contentType.startsWith('image/')) return '🖼️';
        if (contentType.startsWith('audio/')) return '🎵';
        if (contentType.startsWith('video/')) return '🎬';
        if (contentType.includes('pdf')) return '📕';
        if (contentType.includes('zip') || contentType.includes('archive')) return '📦';
        if (contentType.includes('text')) return '📝';
        if (contentType.includes('spreadsheet') || contentType.includes('excel')) return '📊';
        if (contentType.includes('presentation') || contentType.includes('powerpoint')) return '🖥️';
        if (contentType.includes('document') || contentType.includes('word')) return '📄';
        return '💾';
      },
  
      onDownloadClick() {
        this.$emit('download-file', this.file.Id);
      },
  
      onDeleteClick() {
         this.$emit('delete-file', this.file.Id);
      }
    }
  }
  </script>
  
  <style scoped>
  .file-list-item {
    display: flex;
    align-items: center;
    padding: 12px 15px;
    border: 1px solid #e9ecef;
    border-radius: 5px;
    margin-bottom: 8px;
    background-color: #fff;
    transition: background-color 0.2s ease, box-shadow 0.2s ease;
  }
  
  .file-list-item:hover {
    background-color: #f8f9fa;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.07);
  }
  
  .file-icon {
    font-size: 1.6rem;
    margin-right: 15px;
    color: #6c757d;
    width: 30px;
    text-align: center;
  }
  
  .file-details {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    min-width: 0;
  }
  
  .file-name {
    font-weight: 600;
    color: #343a40;
    margin-bottom: 4px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    cursor: default;
  }
  
  .file-meta {
    font-size: 0.8rem;
    color: #6c757d;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .file-meta span {
      margin-right: 5px;
  }
  .file-meta span:last-child {
      margin-right: 0;
  }
  
  
  .file-actions {
    margin-left: 15px;
    display: flex;
    align-items: center;
    gap: 8px;
  }
  
  .action-button {
    background: none;
    border: 1px solid transparent;
    padding: 4px 6px;
    cursor: pointer;
    font-size: 1rem;
    border-radius: 4px;
    line-height: 1;
    transition: background-color 0.2s ease, color 0.2s ease, transform 0.1s ease;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 28px;
    min-height: 28px;
  }
  .action-button span {
      display: inline-block;
  }
  
  .action-button:hover:not(:disabled) {
    background-color: #e9ecef;
    transform: translateY(-1px);
  }
  .action-button:active:not(:disabled) {
      transform: translateY(0px);
  }
  
  .action-button:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
  
  .download-button { color: #007bff; }
  .download-button:hover:not(:disabled) { color: #0056b3; background-color: rgba(0, 123, 255, 0.1); }
  
  .delete-button { color: #dc3545; }
  .delete-button:hover:not(:disabled) { color: #c82333; background-color: rgba(220, 53, 69, 0.1); }
  
  </style>