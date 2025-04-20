<template>
  <div>
    <h2>Мои файлы</h2>
    <button @click="fetchFiles" :disabled="loading">
      {{ loading ? 'Обновление...' : 'Обновить список' }}
    </button>
     <p v-if="errorMsg" class="error-message">{{ errorMsg }}</p>

    <div v-if="files.length > 0">
      <h3>Файлы группы: {{ userGroup }}</h3>
      <ul class="file-list">
        <li v-for="file in files" :key="file.Id" class="file-item">
          <span class="file-name">{{ file.OriginalName }}</span>
          <span class="file-info">({{ formatBytes(file.SizeBytes) }}, {{ formatDate(file.UploadedAt) }})</span>
          <button @click="downloadFile(file.Id, file.OriginalName)" :disabled="downloading === file.Id">
            {{ downloading === file.Id ? 'Скачивание...' : 'Скачать' }}
          </button>
        </li>
      </ul>
    </div>
     <p v-else-if="!loading && !errorMsg">У вас пока нет загруженных файлов.</p>
  </div>
</template>

<script>
import axios from "axios";

export default {
  name: "MyFiles",
  data() {
    return {
      files: [],
      loading: false,
      downloading: null,
      errorMsg: "",
    };
  },
   computed: {
        userGroup() {
            return localStorage.getItem('userGroup') || 'N/A';
        }
   },
  created() {
    this.fetchFiles();
  },
  methods: {
    async fetchFiles() {
      if (this.loading) return;
      this.loading = true;
      this.errorMsg = "";
      this.files = [];

      try {
         const apiUrl = (this.$apiBaseUrl || 'http://localhost:5001/api') + '/file/my-files';
        const resp = await axios.get(apiUrl);
        this.files = resp.data;
      } catch (err) {
        console.error("Error fetching files:", err);
        this.errorMsg = "Ошибка при получении списка файлов.";
         if (err.response && err.response.status === 401) {
             this.errorMsg += " Пожалуйста, войдите снова.";
         }
      } finally {
        this.loading = false;
      }
    },
    async downloadFile(fileId, originalName) {
       if (this.downloading) return;
       this.downloading = fileId;
       this.errorMsg = "";

      try {
         const apiUrl = (this.$apiBaseUrl || 'http://localhost:5001/api') + `/file/download/${fileId}`;
        const resp = await axios.get(apiUrl, {
          responseType: "blob"
        });
        const url = window.URL.createObjectURL(new Blob([resp.data]));
        const link = document.createElement("a");
        link.href = url;
        link.setAttribute("download", originalName || `file-${fileId}`);
        document.body.appendChild(link);
        link.click();

         window.URL.revokeObjectURL(url);
         link.remove();

      } catch (err) {
        console.error("Error downloading file:", err);
        this.errorMsg = `Ошибка при скачивании файла "${originalName}".`;
         if (err.response && err.response.status === 403) {
             this.errorMsg += " Недостаточно прав.";
         } else if (err.response && err.response.status === 404) {
              this.errorMsg = `Файл "${originalName}" не найден на сервере.`;
         } else if (err.response && err.response.status === 401) {
             this.errorMsg = "Ошибка авторизации. Попробуйте войти снова.";
         }
      } finally {
         this.downloading = null;
      }
    },
     formatBytes(bytes, decimals = 2) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const dm = decimals < 0 ? 0 : decimals;
        const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
    },
    formatDate(dateString) {
        if (!dateString) return '';
        const options = { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' };
        return new Date(dateString).toLocaleDateString(undefined, options);
    }
  }
};
</script>

<style scoped>
button { margin-right: 10px; margin-bottom: 10px; padding: 8px 12px; cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: 0.6; }
.error-message { color: red; margin-top: 10px; }
.file-list { list-style: none; padding: 0; }
.file-item {
  border: 1px solid #eee;
  padding: 10px 15px;
  margin-bottom: 10px;
  border-radius: 4px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
}
.file-name { font-weight: bold; margin-right: 15px; }
.file-info { color: #666; font-size: 0.9em; margin-right: 15px; }
.file-item button { background-color: #17a2b8; color: white; border: none; border-radius: 4px; }
.file-item button:hover:not(:disabled) { background-color: #138496; }
</style>