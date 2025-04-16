<template>
    <div class="dashboard-layout">
      <header class="dashboard-header">
        <div class="logo-title">
          <h1>Панель пользователя</h1>
        </div>
            <div class="user-info">
            <span>Здравствуйте, <strong>{{ username }}</strong>!</span>
            <span class="user-group">(Группы: {{ userGroups }})</span>
            <button @click="logout" class="logout-button">Выйти</button>
        </div>
      </header>
  
      <nav class="dashboard-nav">
        <router-link :to="{ name: 'MyFiles' }" class="nav-link">Мои файлы</router-link>
        <router-link :to="{ name: 'UploadFile' }" class="nav-link">Загрузить файл</router-link>
      </nav>
  
      <main class="dashboard-content">
        <router-view />
      </main>
  
      <footer class="dashboard-footer">
        <p>© {{ new Date().getFullYear() }} Ваше Приложение</p>
      </footer>
    </div>
  </template>
  
  <script>
  
  export default {
    name: 'UserDashboard',
    computed: {
    username() { return localStorage.getItem('username') || 'Пользователь'; },
    userGroups() {
      const groupsJson = localStorage.getItem('userGroups');
      try {
        const groups = JSON.parse(groupsJson || '[]');
        return groups.join(', ') || 'Нет групп';
      } catch {
        return 'Ошибка групп';
      }
    }
  },
    methods: {
      logout() {
        localStorage.removeItem('jwtToken');
        localStorage.removeItem('userRole');
        localStorage.removeItem('userGroup');
        localStorage.removeItem('username');
        this.$router.push({ name: 'Login' });
      }
    }
  }
  </script>
  
  <style scoped>
  .dashboard-layout {
    display: flex;
    flex-direction: column;
    min-height: 100vh;
  }
  
  .dashboard-header {
    background-color: #ffffff;
    padding: 15px 30px;
    border-bottom: 1px solid #dee2e6;
    display: flex;
    justify-content: space-between;
    align-items: center;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
  }
  
  .logo-title {
      display: flex;
      align-items: center;
  }
  
  .logo-title h1 {
    margin: 0;
    font-size: 1.6rem;
    color: #343a40;
  }
  
  .user-info {
    display: flex;
    align-items: center;
    gap: 15px;
    color: #495057;
  }
  
  .user-group {
      font-size: 0.9em;
      color: #6c757d;
  }
  
  .logout-button {
    padding: 8px 15px;
    cursor: pointer;
    background-color: #6c757d;
    color: white;
    border: none;
    border-radius: 4px;
    transition: background-color 0.2s ease;
    font-size: 0.9rem;
  }
  
  .logout-button:hover {
    background-color: #5a6268;
  }
  
  .dashboard-nav {
    background-color: #f8f9fa;
    padding: 12px 30px;
    border-bottom: 1px solid #dee2e6;
  }
  
  .nav-link {
    margin-right: 20px;
    text-decoration: none;
    color: #007bff;
    font-size: 1rem;
    padding: 5px 0;
    transition: color 0.2s ease;
  }
  
  .nav-link:hover {
    color: #0056b3;
  }
  
  .nav-link.router-link-exact-active {
    font-weight: bold;
    color: #0056b3;
    border-bottom: 2px solid #0056b3;
  }
  
  .dashboard-content {
    padding: 30px;
    flex-grow: 1;
    background-color: #f4f7f6;
  }
  
  .dashboard-footer {
    background-color: #343a40;
    color: #f8f9fa;
    text-align: center;
    padding: 15px 0;
    font-size: 0.9rem;
    margin-top: auto;
  }
  .dashboard-footer p {
      margin: 0;
  }
  </style>