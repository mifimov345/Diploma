<template>
  <div class="file-upload-container"> <!-- Добавлен класс для контейнера -->
    <h2>Загрузка файла</h2>
    <div v-if="uploadMessage" :class="['message', messageType]">
        {{ uploadMessage }}
    </div>
    <form @submit.prevent="uploadFile" class="upload-form"> <!-- Добавлен класс для формы -->
      <div class="form-group">
         <label for="file-input" class="file-label"> <!-- Добавлен класс для label -->
            <span class="button-like">Выберите файл</span> <!-- Стилизованный элемент -->
            <span v-if="selectedFile" class="file-name-display">{{ selectedFile.name }}</span>
            <span v-else class="no-file-selected">Файл не выбран</span>
         </label>
         <input
            id="file-input"
            type="file"
            @change="onFileChange"
            required
            class="file-input-hidden"
          />
      </div>

      <button type="submit" :disabled="!selectedFile || loading" class="upload-button">
        <span v-if="loading">Загрузка...</span>
        <span v-else>Загрузить</span>
      </button>
    </form>
  </div>
</template>

<script>
import axios from "axios";

export default {
  name: "FileUpload",
  data() {
    return {
      selectedFile: null,
      uploadMessage: "",
      messageType: "success",
      loading: false,
    };
  },
  methods: {
    onFileChange(e) {
      const file = e.target.files[0];
      if (file) {
         this.selectedFile = file;
         this.uploadMessage = "";
      } else {
          this.selectedFile = null;
      }
    },
    async uploadFile() {
      if (!this.selectedFile || this.loading) {
        return;
      }
      this.loading = true;
      this.uploadMessage = "";

      const apiUrl = '/api/file/upload';

      try {
        const formData = new FormData();
        formData.append("file", this.selectedFile);

        console.log(`FileUpload: Sending POST to ${axios.defaults.baseURL}${apiUrl}`);

        const resp = await axios.post(apiUrl, formData);

        this.messageType = "success";
        if (resp.data && resp.data.FileName && resp.data.FileId) {
             this.uploadMessage = `Файл "${resp.data.FileName}" успешно загружен (ID: ${resp.data.FileId}).`;
        } else {
            this.uploadMessage = "Файл успешно загружен.";
            console.warn("Upload response data format might be different:", resp.data);
        }

        this.selectedFile = null;
        const fileInput = document.getElementById('file-input');
        if(fileInput) fileInput.value = null;

      } catch (err) {
        console.error("File upload error:", err);
        this.messageType = "error";
        if (err.response) {
             this.uploadMessage = err.response.data?.message || err.response.data || `Ошибка загрузки (статус ${err.response.status}).`;
             if (err.response.status === 404) {
                 this.uploadMessage = `Ошибка: Конечная точка загрузки не найдена (${axios.defaults.baseURL}${apiUrl}). Проверьте URL.`;
             } else if (err.response.status === 401) {
                this.uploadMessage = "Ошибка: Не авторизован. Попробуйте войти снова.";
             } else if (err.response.status === 413) {
                 this.uploadMessage = "Ошибка: Файл слишком большой.";
             } else if (err.response.status === 500) {
                  this.uploadMessage = "Внутренняя ошибка сервера при загрузке файла.";
             }
         } else if (err.request) {
              this.uploadMessage = "Ошибка сети или сервер недоступен.";
         }
          else {
          this.uploadMessage = "Неизвестная ошибка при попытке загрузить файл.";
        }
      } finally {
         this.loading = false;
      }
    }
  }
};
</script>

<style scoped>
.file-upload-container {
  padding: 20px;
  background-color: #fff;
  border-radius: 8px;
  box-shadow: 0 2px 8px rgba(0,0,0,0.08);
  max-width: 600px;
  margin: 20px auto;
}

h2 {
    margin-top: 0;
    margin-bottom: 25px;
    color: #333;
    text-align: center;
}

.upload-form .form-group {
  margin-bottom: 20px;
  text-align: center;
}

.file-label {
  display: inline-block;
  cursor: pointer;
  border: 1px dashed #ccc;
  padding: 20px 30px;
  border-radius: 5px;
  background-color: #f9f9f9;
  transition: background-color 0.2s, border-color 0.2s;
}
.file-label:hover {
  background-color: #f1f1f1;
  border-color: #aaa;
}

.button-like {
  background-color: #007bff;
  color: white;
  padding: 8px 15px;
  border-radius: 4px;
  margin-right: 10px;
  display: inline-block;
}

.file-name-display {
  font-style: italic;
  color: #333;
}

.no-file-selected {
  color: #6c757d;
}

.file-input-hidden {
  width: 0.1px;
  height: 0.1px;
  opacity: 0;
  overflow: hidden;
  position: absolute;
  z-index: -1;
}
.file-input-hidden:focus + .file-label {
   outline: 2px solid #007bff;
   outline-offset: 2px;
}


.upload-button {
  display: block;
  width: 100%;
  padding: 12px 15px;
  background-color: #28a745;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 1rem;
  transition: background-color 0.2s ease;
}
.upload-button:disabled { background-color: #cccccc; cursor: not-allowed; }
.upload-button:hover:not(:disabled) { background-color: #218838; }

.message {
  padding: 12px 15px;
  margin-bottom: 15px;
  border-radius: 4px;
  text-align: center;
  font-size: 0.95rem;
  border: 1px solid transparent;
}
.message.success { background-color: #d4edda; color: #155724; border-color: #c3e6cb; }
.message.error { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
</style>