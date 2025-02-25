<template>
    <div>
      <h2>Загрузка файла</h2>
      <input type="file" @change="onFileChange" />
      <button @click="uploadFile">Загрузить</button>
      <div v-if="uploadResult">
        <p>Результат: {{ uploadResult }}</p>
      </div>
    </div>
  </template>
  
  <script>
  import axios from "axios";
  
  export default {
    name: "FileUpload",
    data() {
      return {
        file: null,
        uploadResult: null
      };
    },
    methods: {
      onFileChange(e) {
        this.file = e.target.files[0];
      },
      async uploadFile() {
        if (!this.file) {
          alert("Выберите файл для загрузки.");
          return;
        }
        const formData = new FormData();
        formData.append("File", this.file);
  
        try {
          const response = await axios.post(
            "http://localhost:5002/api/file/upload", // Порт и эндпоинт для FileService
            formData,
            {
              headers: { "Content-Type": "multipart/form-data" }
            }
          );
          this.uploadResult = response.data;
        } catch (error) {
          console.error(error);
          this.uploadResult = "Ошибка при загрузке файла";
        }
      }
    }
  };
  </script>
  