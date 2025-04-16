<template>
    <div>
      <h2>Админская страница</h2>
      <h3>Создание нового пользователя</h3>
      <div>
        <label>Username:</label>
        <input v-model="newUserUsername" />
      </div>
      <div>
        <label>Password:</label>
        <input type="password" v-model="newUserPassword" />
      </div>
      <div>
        <label>Group:</label>
        <input v-model="newUserGroup" />
      </div>
      <button @click="createUser">Создать</button>
      <p>{{ message }}</p>
    </div>
  </template>
  
  <script>
  import axios from 'axios';
  
  export default {
    name: "AdminPage",
    data() {
      return {
        newUserUsername: "",
        newUserPassword: "",
        newUserGroup: "",
        message: ""
      };
    },
    created() {
      // Пропишем Authorization
      const token = localStorage.getItem("jwtToken");
      if (token) {
        axios.defaults.headers.common["Authorization"] = `Bearer ${token}`;
      }
    },
    methods: {
      async createUser() {
        try {
          const response = await axios.post("http://localhost:5000/api/Auth/create-user", {
            username: this.newUserUsername,
            password: this.newUserPassword,
            group: this.newUserGroup
          });
          this.message = response.data; 
        } catch (err) {
          console.error(err);
          this.message = "Ошибка при создании пользователя или нет прав";
        }
      }
    }
  };
  </script>
  