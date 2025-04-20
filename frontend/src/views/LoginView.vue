<template>
    <div class="login-view">
      <div class="login-container">
        <h2>Вход в систему</h2>
        <form @submit.prevent="handleLogin" class="login-form">
          <div class="form-group">
            <label for="username">Имя пользователя:</label>
            <input
              id="username"
              type="text"
              v-model="username"
              required
              :disabled="isLoading"
              placeholder="например, admin или user1"
            />
          </div>
          <div class="form-group">
            <label for="password">Пароль:</label>
            <input
              id="password"
              type="password"
              v-model="password"
              required
              :disabled="isLoading"
              placeholder="пароль"
            />
          </div>
          <button type="submit" :disabled="isLoading" class="login-button">
            <span v-if="isLoading">Вход...</span>
            <span v-else>Войти</span>
          </button>
          <p v-if="errorMessage" class="error-message">{{ errorMessage }}</p>
        </form>
      </div>
    </div>
  </template>
  
  <script>
  import axios from 'axios';
  
  export default {
    name: 'LoginView',
    data() {
      return {
        username: '',
        password: '',
        errorMessage: '',
        isLoading: false,
      };
    },
    methods: {
      async handleLogin() {
        if (this.isLoading) return;
        this.isLoading = true;
        this.errorMessage = '';
  
        try {
          const response = await axios.post('api/auth/login', {
              username: this.username,
              password: this.password,
            });
            console.log('>>> LOGIN RESPONSE RAW DATA:', response.data);

            const { Token, Role, Groups, Username, Id } = response.data;
            console.log(">>> Extracted Id from response:", Id); // Проверяем извлеченное значение

            localStorage.setItem("jwtToken", Token);
            localStorage.setItem("userRole", Role);
            localStorage.setItem("username", Username);
            localStorage.setItem("userGroups", JSON.stringify(Groups || []));
            localStorage.setItem("userId", Id);
            console.log("Saving userId to localStorage:", Id); // <-- Оставьте этот лог для проверки

            console.log(">>> Saved userId:", Id); // Лог перед чтением
            console.log(">>> Read userId from localStorage immediately:", localStorage.getItem('userId'));
            console.log("LOGIN: Attempting to set token:", Token);
            console.log("LOGIN: Token READ from localStorage after set:", localStorage.getItem('jwtToken'));

            await new Promise(resolve => setTimeout(resolve, 50));

            this.username = '';
            this.password = '';

            if (Role === 'SuperAdmin' || Role === 'Admin') {
              this.$router.push({ name: 'AdminFiles' });
            } else {
              this.$router.push({ name: 'MyFiles' });
            }
  
        } catch (error) {
          console.error('Login failed:', error);
          if (error.response) {
            console.error(">>> LOGIN ERROR RESPONSE:", error.response);
              if (error.response.status === 401) {
                   if (error.response.data && typeof error.response.data === 'string') {
                       this.errorMessage = error.response.data;
                   } else {
                      this.errorMessage = 'Неверное имя пользователя или пароль.';
                   }
              } else if (error.response.data && error.response.data.message) {
                  this.errorMessage = `Ошибка входа: ${error.response.data.message}`;
              }
              else {
                  this.errorMessage = `Ошибка входа (статус ${error.response.status}). Попробуйте позже.`;
                  console.error(">>> LOGIN NETWORK/REQUEST ERROR:", error);
              }
          } else if (error.request) {
              this.errorMessage = 'Не удается подключиться к серверу. Проверьте сеть или обратитесь к администратору.';
          } else {
            // Ошибка при настройке запроса
            this.errorMessage = 'Произошла ошибка при попытке входа.';
          }
        } finally {
          this.isLoading = false;
        }
      },
    },
  };
  </script>
  
  <style scoped>
  .login-view {
    display: flex;
    justify-content: center;
    align-items: center;
    min-height: 100vh;
    background-color: #f4f7f6;
  }
  
  .login-container {
    background-color: #fff;
    padding: 30px 40px;
    border-radius: 8px;
    box-shadow: 0 4px 15px rgba(0, 0, 0, 0.1);
    width: 100%;
    max-width: 400px;
    text-align: center;
  }
  
  h2 {
    margin-bottom: 25px;
    color: #333;
  }
  
  .login-form .form-group {
    margin-bottom: 20px;
    text-align: left;
  }
  
  .login-form label {
    display: block;
    margin-bottom: 8px;
    font-weight: bold;
    color: #555;
  }
  
  .login-form input {
    width: 100%;
    padding: 12px;
    border: 1px solid #ccc;
    border-radius: 4px;
    box-sizing: border-box;
    font-size: 1rem;
  }
  
  .login-form input:focus {
    border-color: #007bff;
    outline: none;
    box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.25);
  }
  
  .login-button {
    width: 100%;
    padding: 12px 15px;
    background-color: #007bff;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 1rem;
    transition: background-color 0.2s ease;
  }
  
  .login-button:hover:not(:disabled) {
    background-color: #0056b3;
  }
  
  .login-button:disabled {
    background-color: #cccccc;
    cursor: not-allowed;
  }
  
  .error-message {
    color: #dc3545;
    margin-top: 20px;
    font-size: 0.9rem;
    min-height: 1.2em;
  }
  </style>