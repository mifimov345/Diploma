<template>
  <div class="dashboard-layout admin-dashboard">
    <header class="dashboard-header admin-header">
      <div class="logo-title">
        <h1>Панель Администратора</h1>
      </div>
      <div class="user-info">
        <span>Администратор: <strong>{{ username }}</strong></span>
        <button @click="logout" class="logout-button">Выйти</button>
      </div>
    </header>

    <nav class="dashboard-nav admin-nav">
      <router-link :to="{ name: 'AdminFiles' }" class="nav-link">Обзор файлов</router-link>
      <router-link :to="{ name: 'AdminUsers' }" class="nav-link">Пользователи</router-link>
      <router-link v-if="isAdminOrSuperAdmin" :to="{ name: 'AdminGroups' }" class="nav-link">Группы</router-link>
      <span class="nav-separator">|</span>
      <router-link :to="{ name: 'AdminMyFiles' }" class="nav-link">Мои файлы</router-link>
      <router-link :to="{ name: 'AdminUploadFile' }" class="nav-link">Загрузить</router-link>
    </nav>

    <main class="dashboard-content">
      <router-view />
    </main>

    <footer class="dashboard-footer admin-footer">
       <p>© {{ new Date().getFullYear() }} Админ-панель</p>
    </footer>
  </div>
</template>

<script>
export default {
  name: 'AdminDashboard',
  computed: {
    username() { return localStorage.getItem('username') || 'Admin'; },
    userRole() { return localStorage.getItem('userRole'); },
    isSuperAdmin() { return this.userRole === 'SuperAdmin'; },
    isAdminOrSuperAdmin() { return this.userRole === 'Admin' || this.userRole === 'SuperAdmin'; }
  },
  methods: {
    logout() {
        localStorage.removeItem('jwtToken');
        localStorage.removeItem('userRole');
        localStorage.removeItem('userGroups');
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
.logout-button {
  padding: 8px 15px;
  cursor: pointer;
  background-color: #dc3545;
  color: white;
  border: none;
  border-radius: 4px;
  transition: background-color 0.2s ease;
  font-size: 0.9rem;
}
.logout-button:hover {
  background-color: #c82333;
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

.admin-header {
  background-color: #e9ecef;
}

.admin-nav {
  background-color: #dee2e6;
}

.nav-separator {
    margin: 0 10px;
    color: #6c757d;
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

.admin-footer {
    background-color: #495057;
}
</style>