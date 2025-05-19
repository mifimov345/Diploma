<template>
  <div class="admin-user-management">
    <h2>Управление пользователями</h2>

    <section v-if="canCreateUsers" class="create-user-section">
       <h3>{{ isSuperAdmin ? 'Создать пользователя или администратора' : 'Создать нового пользователя' }}</h3>
       <form @submit.prevent="createUser" class="create-user-form">
         <div class="form-row">
             <div class="form-group">
               <label for="new-username">Имя пользователя:</label>
               <input id="new-username" v-model="newUser.username" required :disabled="isLoading" placeholder="Логин"/>
             </div>
             <div class="form-group">
               <label for="new-password">Пароль:</label>
               <input id="new-password" type="password" v-model="newUser.password" required :disabled="isLoading" placeholder="Надежный пароль"/>
             </div>
         </div>
          <div class="form-row">
             <div class="form-group">
               <label for="new-role">Роль:</label>
               <select id="new-role" v-model="newUser.role" required :disabled="isLoading || !isSuperAdmin">
                 <option value="User">User (Пользователь)</option>
                 <option v-if="isSuperAdmin" value="Admin">Admin (Администратор)</option>
               </select>
             </div>
             <div class="form-group">
             </div>
          </div>
           <div class="form-row">
              <div class="form-group full-width">
                 <label for="group-assignment">{{ isSuperAdmin ? 'Группы (можно несколько):' : 'Группа (выберите одну):' }}</label>
                  <div v-if="isSuperAdmin && availableGroups.length > 0">
                    <label v-for="group in availableGroups" :key="group" class="checkbox-label">
                        <input type="checkbox" :value="group" v-model="newUser.groups" :disabled="isLoading"/>
                        {{ group }}
                      </label>
                  </div>
                  <div v-else-if="!isSuperAdmin && adminAssignableGroups.length > 0">
                      <select id="group-assignment" v-model="selectedGroupForNewUser" required :disabled="isLoading">
                           <option disabled value="">-- Выберите группу --</option>
                           <option v-for="group in adminAssignableGroups" :key="group" :value="group">
                               {{ group }}
                           </option>
                      </select>
                  </div>
                  <div v-else-if="isSuperAdmin && availableGroups.length === 0">
                       <p>Сначала <router-link :to="{name: 'AdminGroups'}">создайте группы</router-link>.</p>
                  </div>
                  <div v-else-if="!isSuperAdmin && adminAssignableGroups.length === 0">
                       <p>Вы должны состоять хотя бы в одной группе, чтобы создавать пользователей.</p>
                  </div>
                  <small v-if="isSuperAdmin && newUser.role === 'Admin'">*Администратору необходимо назначить хотя бы одну группу.</small>
                  <small v-if="!isSuperAdmin && adminAssignableGroups.length > 0">*Пользователь будет добавлен в выбранную вами группу.</small>
              </div>
           </div>
         <button type="submit" :disabled="!canSubmitCreateUser" class="create-button">
           <span v-if="isLoading">Создание...</span>
           <span v-else>Создать</span>
         </button>
       </form>
        <div v-if="message" :class="['message', messageType]"> {{ message }} </div>
        <div v-if="credsError" class="message error"> {{ credsError }} </div> <!-- Ошибка смены УЗ -->
     </section>
     <div v-else> <p>У вас недостаточно прав для создания пользователей.</p> </div>

    <section class="user-list-section">
      <h3>Существующие пользователи</h3>
      <button @click="fetchUsers" :disabled="isUserListLoading" class="refresh-button">
          {{ isUserListLoading ? 'Обновление...' : 'Обновить список' }}
      </button>
      <div v-if="userListError" class="error-message">{{ userListError }}</div>

      <table v-if="users.length > 0" class="user-table">
         <thead>
            <tr>
                <th>ID</th>
                <th>Имя</th>
                <th>Роль</th>
                <th>Группы</th>
                <th v-if="isSuperAdmin">Создан адм. ID</th>
                <th>Действия</th>
            </tr>
         </thead>
         <tbody>
            <tr v-for="user in users" :key="user.Id">
                <td>{{ user.Id }}</td>
                <td>{{ user.Username }}</td>
                <td>{{ user.Role }}</td>
                <td>{{ user.Groups && user.Groups.length > 0 ? user.Groups.join(', ') : '-' }}</td>
                <td v-if="isSuperAdmin">{{ user.CreatedByAdminId || '-' }}</td>
                <td class="actions-cell">
                   <button v-if="canEditGroups(user)" @click="openEditGroupsModal(user)" class="action-button edit-button" title="Изменить группы">⚙️</button>
                   <button v-if="canEditCredentials(user)" @click="openChangeUsernameModal(user)" :disabled="isUpdatingCreds" class="action-button change-button" title="Сменить логин">👤</button>
                   <button v-if="canEditCredentials(user)" @click="openChangePasswordModal(user)" :disabled="isUpdatingCreds" class="action-button change-button" title="Сменить пароль">🔑</button>
                   <button v-if="canDeleteUser(user)" @click="deleteUser(user.Id, user.Username)" :disabled="isUpdatingCreds" class="action-button delete-button" title="Удалить пользователя">🗑️</button>
                </td>
            </tr>
         </tbody>
      </table>
       <div v-else-if="!isUserListLoading && !userListError">
           <p>Пользователи не найдены.</p>
       </div>
        <div v-if="isUserListLoading" class="loading-indicator">Загрузка списка...</div>
    </section>

    <div v-if="showEditModal" class="modal-overlay" @click.self="closeEditGroupsModal">
        <div class="modal-content">
            <h4>Изменить группы для пользователя: {{ editingUser.Username }}</h4>
             <div v-if="availableGroups.length > 0" class="checkbox-group">
                 <label v-for="group in availableGroups" :key="group" class="checkbox-label">
                   <input type="checkbox" :value="group" v-model="editingUserGroups" :disabled="isUpdatingGroups"/>
                   {{ group }}
                 </label>
             </div>
             <p v-else>Нет доступных групп.</p>
             <p v-if="editingUser.Role === 'Admin' && editingUserGroups.length === 0" class="error-message small">Администратор должен состоять хотя бы в одной группе!</p>
             <div class="modal-actions">
                 <button @click="updateUserGroups" :disabled="isUpdatingGroups || (editingUser.Role === 'Admin' && editingUserGroups.length === 0)" class="save-button"> {{ isUpdatingGroups ? 'Сохранение...' : 'Сохранить'}} </button>
                 <button @click="closeEditGroupsModal" :disabled="isUpdatingGroups" class="cancel-button">Отмена</button>
             </div>
             <div v-if="editGroupsError" class="error-message small">{{ editGroupsError }}</div>
        </div>
    </div>

  </div>
</template>

<script>
import axios from 'axios';

export default {
  name: 'AdminUserManagement',
  data() {
    return {
      newUser: { username: '', password: '', role: 'User', groups: [] },
      selectedGroupForNewUser: '',
      isLoading: false,
      message: '', messageType: 'success',
      availableGroups: [],

      users: [],
      isUserListLoading: false,
      userListError: '',

      showEditModal: false,
      editingUser: null,
      editingUserGroups: [],
      isUpdatingGroups: false,
      editGroupsError: '',

      // Смена УЗ
      editingUserForCreds: null,
      newUsername: '',
      newPassword: '',
      isUpdatingCreds: false,
      credsError: '',

      currentUserId: null,
      currentUserRole: null,
      currentUserGroups: [],
    };
  },
  computed: {
    isSuperAdmin() { return this.currentUserRole === 'SuperAdmin'; },
    isAdmin() { return this.currentUserRole === 'Admin'; },
    canCreateUsers() { return this.isSuperAdmin || this.isAdmin; },

    adminAssignableGroups() {
    if (this.isSuperAdmin) {
        return this.availableGroups;
    }
    const adminGroupsSet = new Set(this.currentUserGroups || []);
    console.log("Filtering availableGroups:", this.availableGroups, "using adminGroupsSet:", adminGroupsSet);
    const result = (this.availableGroups || []).filter(g => adminGroupsSet.has(g));
    console.log("Resulting adminAssignableGroups:", result);
    return result;
},

    canSubmitCreateUser() {
        if (this.isLoading) return false;
        if (!this.newUser.username || !this.newUser.password) return false;

        if (this.isSuperAdmin) {
             if (this.newUser.role === 'Admin' && this.newUser.groups.length === 0) {
                 return false;
             }
        } else {
             if (!this.selectedGroupForNewUser) {
                 return false;
             }
              if (this.adminAssignableGroups.length === 0) {
                  return false;
              }
        }
        return true;
    }
  },
  methods: {
    async fetchAvailableGroups() {
        this.message = ''; this.messageType = 'success';
      try {
          const response = await axios.get('/api/auth/groups');
          this.availableGroups = response.data || [];
          console.log("Fetched availableGroups:", this.availableGroups);
          if (!this.isSuperAdmin && this.availableGroups.length === 0) {
              console.log("Admin has no groups to assign (fetched list is empty).");
          }
        } catch (err) {
            this.message = 'Не удалось загрузить список групп для формы.';
            this.messageType = 'error';
            this.availableGroups = [];
        }
    },
    async fetchUsers() {
      this.isUserListLoading = true;
      this.userListError = '';
      this.users = [];
      try {
        const response = await axios.get('/api/auth/users');
        this.users = response.data || [];
      } catch (err) {
        this.userListError = 'Не удалось загрузить список пользователей.';
      } finally {
        this.isUserListLoading = false;
      }
    },

    async createUser() {
      if (!this.canSubmitCreateUser) return;

      this.isLoading = true;
      this.message = '';
      this.credsError = '';

      const payload = {
        username: this.newUser.username,
        password: this.newUser.password,
      };

      if (this.isSuperAdmin) {
         payload.role = this.newUser.role;
         payload.groups = this.newUser.groups;
      }
      else {
          payload.role = 'User';
          payload.groups = [this.selectedGroupForNewUser];
      }

      try {
        const response = await axios.post('/api/auth/users', payload);
        this.message = `Пользователь "${response.data.Username}" (${response.data.Role}) успешно создан.`;
        this.messageType = 'success';
        this.newUser.username = '';
        this.newUser.password = '';
        this.newUser.role = this.isSuperAdmin ? 'User' : 'User';
        this.newUser.groups = [];
        this.selectedGroupForNewUser = '';
        await this.fetchUsers();
      } catch (err) {
        this.messageType = 'error';
        this.message = `Ошибка создания: ${err.response?.data?.message || err.message}`;
      } finally {
        this.isLoading = false;
      }
    },

    canEditGroups(user) {
        if (!user) {
             console.error("canEditGroups called with undefined user");
             return false;
        }
        if (this.isSuperAdmin && user.Role !== 'SuperAdmin') return true; 
        if (this.isAdmin && user.Role === 'User' && user.Groups.some(ug => this.currentUserGroups.includes(ug))) {
          return true;
        }
    return false;
    },
    openEditGroupsModal(user) {
               if (!user) {
            console.error("openEditGroupsModal called with undefined user");
            return;
         }
         if (!this.canEditGroups(user)) return;
         this.editingUser = { ...user };
         this.editingUserGroups = [...(user.Groups || [])];
         this.editGroupsError = '';
         this.showEditModal = true;
         if (this.availableGroups.length === 0 && this.isSuperAdmin) {
             this.fetchAvailableGroups();
         }
     },
    closeEditGroupsModal() {
         this.showEditModal = false;
         this.editingUser = null;
         this.editingUserGroups = [];
         this.editGroupsError = '';
     },
     async updateUserGroups() {
         if (!this.editingUser || this.isUpdatingGroups) return;

         if (this.editingUser.Role === 'Admin' && this.editingUserGroups.length === 0) {
             this.editGroupsError = "Администратор должен состоять хотя бы в одной группе!";
             return;
         }
         this.isUpdatingGroups = true; this.editGroupsError = '';

         try {
             await axios.put(`/api/auth/users/${this.editingUser.Id}/groups`, this.editingUserGroups);
             this.closeEditGroupsModal();
             await this.fetchUsers();
             this.message = `Группы для пользователя ${this.editingUser.Username} обновлены.`;
             this.messageType = 'success';
         } catch (err) {
              this.editGroupsError = `Ошибка обновления групп: ${err.response?.data?.message || err.message}`;
         } finally {
             this.isUpdatingGroups = false;
         }
     },

     canEditCredentials(user) {
        if (!user || user.Id === this.currentUserId || user.Role === 'SuperAdmin') return false;
        if (this.isSuperAdmin) {
             return user.Role === 'Admin' || user.Role === 'User';
        }
        if (this.isAdmin) {
             return user.Role === 'User' && user.Groups.some(ug => this.currentUserGroups.includes(ug));
        }
        return false;
    },
    async openChangeUsernameModal(user) {
        if (!this.canEditCredentials(user)) return;
        const input = prompt(`Введите новый логин для пользователя "${user.Username}" (ID: ${user.Id}):`, user.Username);
        if (input === null) { this.credsError = ''; return; } // Отмена
        if (input.trim() === '') { this.credsError = 'Логин не может быть пустым.'; return; }
        if (input.trim() === user.Username) { this.credsError = 'Новый логин совпадает со старым.'; return; }

        this.newUsername = input.trim();
        this.editingUserForCreds = user;
        await this.changeUsername();
    },
    async openChangePasswordModal(user) {
        if (!this.canEditCredentials(user)) return;
        const input = prompt(`Введите НОВЫЙ пароль для пользователя "${user.Username}" (ID: ${user.Id}):`);
        if (input === null) { this.credsError = ''; return; }
        if (input === '') { this.credsError = "Пароль не может быть пустым."; return; }

        this.newPassword = input;
        this.editingUserForCreds = user;
        await this.changePassword();
    },
    async changeUsername() {
        if (!this.editingUserForCreds || !this.newUsername || this.isUpdatingCreds) return;
        this.isUpdatingCreds = true; this.credsError = ''; this.message = '';
        try {
            await axios.put(`/api/auth/users/${this.editingUserForCreds.Id}/username`, { newUsername: this.newUsername });
            this.message = `Логин для ID ${this.editingUserForCreds.Id} изменен на "${this.newUsername}".`;
            this.messageType = 'success';
            await this.fetchUsers(); 
        } catch (err) {
            //console.error("Error changing username:", err);
            this.credsError = `Ошибка смены логина: ${err.response?.data?.message || err.message}`;
        } finally {
            this.isUpdatingCreds = false; this.editingUserForCreds = null; this.newUsername = '';
        }
    },
    async changePassword() {
        if (!this.editingUserForCreds || !this.newPassword || this.isUpdatingCreds) return;
        this.isUpdatingCreds = true; this.credsError = ''; this.message = '';
        try {
            await axios.put(`/api/auth/users/${this.editingUserForCreds.Id}/password`, { newPassword: this.newPassword });
            this.message = `Пароль для пользователя ID ${this.editingUserForCreds.Id} успешно изменен.`;
            this.messageType = 'success';
        } catch (err) {
             console.error("Error changing password:", err);
             this.credsError = `Ошибка смены пароля: ${err.response?.data?.message || err.message}`;
        } finally {
            this.isUpdatingCreds = false; this.editingUserForCreds = null; this.newPassword = '';
        }
    },

    canDeleteUser(user) {
         if (!user || user.Id === this.currentUserId || user.Role === 'SuperAdmin') return false;
         if (this.isSuperAdmin) return true;
         if (this.isAdmin) {
             return user.Role === 'User' && user.Groups.some(ug => this.currentUserGroups.includes(ug));
         }
         return false;
     },
    async deleteUser(userId, username) {
      if (!this.canDeleteUser({ Id: userId })) return;
      if (!confirm(`Вы уверены, что хотите удалить пользователя "${username}" (ID: ${userId})? Это действие необратимо.`)) {
        return;
      }
      this.message = ''; this.credsError = '';
      try {
        await axios.delete(`/api/auth/users/${userId}`);
        this.message = `Пользователь "${username}" (ID: ${userId}) удален.`;
        this.messageType = 'success';
        await this.fetchUsers();
      } catch (err) {
        //console.error('Error deleting user:', err);
        this.message = `Ошибка удаления: ${err.response?.data?.message || err.message}`;
        this.messageType = 'error';
      } finally {
         // Сброс индикатора удаления
      }
    },

    loadCurrentUser() {
        this.currentUserId = parseInt(localStorage.getItem('userId') || '0');
        this.currentUserRole = localStorage.getItem('userRole');
        try { this.currentUserGroups = JSON.parse(localStorage.getItem('userGroups') || '[]'); }
        catch { this.currentUserGroups = []; }
         if (!this.isSuperAdmin) {
             this.newUser.role = 'User';
         }
    }
  },
  created() {
     this.loadCurrentUser();
     this.fetchUsers();
     this.fetchAvailableGroups();
  }
};
</script>

<style scoped>
.admin-user-management { padding: 20px; background-color: #fff; border-radius: 8px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08); }
h2 { margin-top: 0; margin-bottom: 25px; color: #333; border-bottom: 1px solid #eee; padding-bottom: 10px; }

.create-user-section { margin-bottom: 30px; padding: 25px; border: 1px solid #e0e0e0; border-radius: 6px; background-color: #fdfdfd; }
.user-list-section { margin-top: 30px; padding-top: 20px; border-top: 1px solid #eee;}
.create-user-section h3, .user-list-section h3 { margin-top: 0; margin-bottom: 20px; color: #444; }

.create-user-form .form-row { display: flex; gap: 20px; margin-bottom: 15px; flex-wrap: wrap; }
.create-user-form .form-group { flex: 1; min-width: 200px; margin-bottom: 5px; }
.create-user-form .form-group.full-width { flex-basis: 100%; min-width: auto;}
.create-user-form label { display: block; margin-bottom: 8px; font-weight: 600; color: #555; font-size: 0.9rem; }
.create-user-form input, .create-user-form select { width: 100%; padding: 10px 12px; border: 1px solid #ccc; border-radius: 4px; box-sizing: border-box; font-size: 0.95rem; }
.create-user-form input:focus, .create-user-form select:focus { border-color: #007bff; outline: none; box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.2); }
.checkbox-group { display: flex; flex-wrap: wrap; gap: 10px 20px; margin-top: 5px; padding: 10px; border: 1px solid #eee; border-radius: 4px; background-color: #f9f9f9; }
.checkbox-group label { margin-right: 15px; }
.checkbox-label { display: inline-flex; align-items: center; cursor: pointer; margin: 0;}
.checkbox-label input { margin-right: 5px; cursor: pointer; }
.create-user-form small { display: block; margin-top: 5px; font-size: 0.85em; color: #6c757d; }
.create-button { padding: 12px 25px; background-color: #28a745; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 1rem; transition: background-color 0.2s ease; margin-top: 10px; }
.create-button:hover:not(:disabled) { background-color: #218838; }
.create-button:disabled { background-color: #cccccc; cursor: not-allowed; }
.create-user-form select[disabled] { background-color: #e9ecef; cursor: not-allowed; } 

.message { padding: 12px 15px; margin-top: 20px; border-radius: 4px; border: 1px solid transparent; font-size: 0.95rem; }
.message.success { background-color: #d4edda; color: #155724; border-color: #c3e6cb; }
.message.error { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
.error-message { /* Стиль для ошибки списка */ text-align: center; padding: 15px; margin-top: 15px; border-radius: 4px; background-color: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
.error-message.small { /* Для модалки */ padding: 8px; margin-top: 10px; text-align: left;}

.refresh-button { margin-bottom: 15px; padding: 8px 15px;}
.user-table { width: 100%; border-collapse: collapse; margin-top: 15px; table-layout: fixed; /* Для лучшего контроля ширины */ }
.user-table th, .user-table td { border: 1px solid #ddd; padding: 10px 12px; text-align: left; word-wrap: break-word; /* Перенос длинных строк */ }
.user-table th { background-color: #f2f2f2; font-weight: bold; }
.user-table td { vertical-align: middle; }
.user-table td.actions-cell { text-align: center; width: 150px; /* Фиксированная ширина для кнопок */ white-space: nowrap; /* Запрет переноса кнопок */}
.action-button { background: none; border: none; cursor: pointer; font-size: 1.2rem; padding: 4px; margin: 0 3px; border-radius: 4px; transition: background-color 0.2s; line-height: 1;}
.action-button:hover:not(:disabled) { background-color: #eee; }
.action-button:disabled { opacity: 0.5; cursor: not-allowed; }
.edit-button { color: #ffc107; } /* Желтый */
.change-button { color: #17a2b8; } /* Бирюзовый */
.delete-button { color: #dc3545; } /* Красный */
.loading-indicator { text-align: center; padding: 20px; color: #6c757d; }

/* Стили модального окна (из пред. ответа) */
.modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background-color: rgba(0, 0, 0, 0.6); display: flex; justify-content: center; align-items: center; z-index: 1000; }
.modal-content { background-color: white; padding: 30px; border-radius: 8px; min-width: 400px; max-width: 600px; box-shadow: 0 5px 15px rgba(0,0,0,0.3); }
.modal-content h4 { margin-top: 0; margin-bottom: 20px; }
.modal-content .checkbox-group { margin-top: 15px; margin-bottom: 15px; } /* Отступы внутри модалки */
.modal-actions { margin-top: 25px; display: flex; justify-content: flex-end; gap: 10px; }
.modal-actions button { padding: 10px 20px; border-radius: 4px; cursor: pointer; border: none; }
.save-button { background-color: #28a745; color: white; }
.save-button:disabled { background-color: #ccc; }
.cancel-button { background-color: #6c757d; color: white; }
.cancel-button:disabled { background-color: #ccc; }
</style>