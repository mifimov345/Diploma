<template>
  <div class="login-container">
    <h2>Вход в систему</h2>
    <form @submit.prevent="login">
      <div class="form-group">
        <label for="username">Имя пользователя:</label>
        <input id="username" v-model="username" required />
      </div>
      <div class="form-group">
        <label for="password">Пароль:</label>
        <input id="password" type="password" v-model="password" required />
      </div>
      <button type="submit" :disabled="loading">
        {{ loading ? 'Вход...' : 'Войти' }}
      </button>
      <p class="error-message" v-if="errorMsg">{{ errorMsg }}</p>
    </form>
  </div>
</template>

<script>
import axios from 'axios';

export default {
  name: "LoginView",
  data() {
    return {
      username: "",
      password: "",
      errorMsg: "",
      loading: false,
    };
  },
  methods: {
    async login() {
      if (this.loading) return;
      this.loading = true;
      this.errorMsg = "";

      try {
        const apiUrl = (this.$apiBaseUrl || 'http://localhost:5001/api') + '/auth/login';
        const response = await axios.post(apiUrl, {
          username: this.username,
          password: this.password
        });

        const { Token, Role, Group, Username } = response.data;

        localStorage.setItem("jwtToken", Token);
        localStorage.setItem("userRole", Role);
        localStorage.setItem("userGroup", Group);
        localStorage.setItem("username", Username);

        this.username = "";
        this.password = "";

        if (Role === "Admin") {
          this.$router.push({ name: 'AdminDashboard' });
        } else {
          this.$router.push({ name: 'UserDashboard' });
        }

      } catch (error) {
        console.error("Login error:", error);
        if (error.response && error.response.data && error.response.data.message) {
             this.errorMsg = error.response.data.message;
         } else if (error.response && error.response.status === 401) {
             this.errorMsg = "Неверное имя пользователя или пароль.";
         }
         else {
          this.errorMsg = "Ошибка входа. Попробуйте позже.";
        }
      } finally {
        this.loading = false;
      }
    }
  }
};
</script>

<style scoped>
.login-container {
  max-width: 400px;
  margin: 50px auto;
  padding: 20px;
  border: 1px solid #ccc;
  border-radius: 8px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}
.form-group {
  margin-bottom: 15px;
}
label {
  display: block;
  margin-bottom: 5px;
}
input {
  width: 100%;
  padding: 8px;
  box-sizing: border-box;
  border: 1px solid #ccc;
  border-radius: 4px;
}
button {
  padding: 10px 15px;
  background-color: #007bff;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  width: 100%;
}
button:disabled {
  background-color: #cccccc;
  cursor: not-allowed;
}
button:hover:not(:disabled) {
  background-color: #0056b3;
}
.error-message {
  color: red;
  margin-top: 15px;
  text-align: center;
}
</style>