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
              <input
                id="new-password"
                type="password"
                v-model="newUser.password"
                required
                :disabled="isLoading"
                placeholder="Минимум 6 символов"
                minlength="6"
                />
              <small v-if="newUser.password && newUser.password.length < 6" class="input-error">
                Пароль должен быть не менее 6 символов.
              </small>
            </div>
        </div>
         <div v-if="isSuperAdmin" class="form-row">
            <div class="form-group">
              <label for="new-role">Роль:</label>
              <select id="new-role" v-model="newUser.role" required :disabled="isLoading">
                <option value="User">User (Пользователь)</option>
                <option value="Admin">Admin (Администратор)</option>
              </select>
            </div>
         </div>
          <div v-if="isSuperAdmin" class="form-row">
             <div class="form-group full-width">
                <label for="new-groups">Группы:</label>
                 <div v-if="availableGroups.length > 0" class="checkbox-group">
                     <label v-for="group in availableGroups" :key="group" class="checkbox-label">
                       <input
                         type="checkbox"
                         :value="group"
                         v-model="newUser.groups"
                         :disabled="isLoading"
                       />
                       {{ group }}
                     </label>
                 </div>
                 <div v-else>
                     <p>Сначала <router-link :to="{name: 'AdminGroups'}">создайте группы</router-link>.</p>
                 </div>
                 <small v-if="newUser.role === 'Admin'">*Администратору необходимо назначить хотя бы одну группу.</small>
             </div>
          </div>

        <button type="submit" :disabled="isLoading || (isSuperAdmin && newUser.role === 'Admin' && newUser.groups.length === 0)" class="create-button">
          <span v-if="isLoading">Создание...</span>
          <span v-else>Создать</span>
        </button>
      </form>
       <div v-if="message" :class="['message', messageType]"> {{ message }} </div>
    </section>
    <div v-else>
        <p>У вас недостаточно прав для создания пользователей.</p>
    </div>

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
                <td>{{ user.Groups.join(', ') || '-' }}</td>
                <td v-if="isSuperAdmin">{{ user.CreatedByAdminId || '-' }}</td>
                <td>
                   <button v-if="isSuperAdmin && user.Role !== 'SuperAdmin'" @click="openEditGroupsModal(user)" class="action-button edit-button" title="Изменить группы">⚙️</button>
                   <button
                     v-if="canDeleteUser(user)"
                     @click="deleteUser(user.Id, user.Username)"
                     class="action-button delete-button"
                     title="Удалить пользователя"
                   >🗑️</button>
                </td>
            </tr>
         </tbody>
      </table>
       <div v-else-if="!isUserListLoading && !userListError">
           <p>Пользователи не найдены.</p>
       </div>
    </section>

    <div v-if="showEditModal" class="modal-overlay" @click.self="closeEditGroupsModal">
        <div class="modal-content">
            <h4>Изменить группы для пользователя: {{ editingUser.Username }}</h4>
             <div v-if="availableGroups.length > 0" class="checkbox-group">
                 <label v-for="group in availableGroups" :key="group" class="checkbox-label">
                   <input
                     type="checkbox"
                     :value="group"
                     v-model="editingUserGroups"
                     :disabled="isUpdatingGroups"
                   />
                   {{ group }}
                 </label>
             </div>
             <p v-else>Нет доступных групп.</p>
             <p v-if="editingUser.Role === 'Admin' && editingUserGroups.length === 0" class="error-message">Администратор должен состоять хотя бы в одной группе!</p>

             <div class="modal-actions">
                 <button @click="updateUserGroups" :disabled="isUpdatingGroups || (editingUser.Role === 'Admin' && editingUserGroups.length === 0)" class="save-button">
                    {{ isUpdatingGroups ? 'Сохранение...' : 'Сохранить'}}
                 </button>
                 <button @click="closeEditGroupsModal" :disabled="isUpdatingGroups" class="cancel-button">Отмена</button>
             </div>
             <div v-if="editGroupsError" class="error-message">{{ editGroupsError }}</div>
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
      newUser: {
        username: '',
        password: '',
        role: 'User',
        groups: [],
      },
      isLoading: false,
      message: '',
      messageType: 'success',
      availableGroups: [],

      users: [],
      isUserListLoading: false,
      userListError: '',

      showEditModal: false,
      editingUser: null,
      editingUserGroups: [],
      isUpdatingGroups: false,
      editGroupsError: '',

      currentUserId: null,
      currentUserRole: null,
      currentUserGroups: [],
    };
  },
  computed: {
    isSuperAdmin() {
      return this.currentUserRole === 'SuperAdmin';
    },
    isAdmin() {
      return this.currentUserRole === 'Admin';
    },
     canCreateUsers() {
       return this.isSuperAdmin || this.isAdmin;
     },
  },
  methods: {
     async fetchAvailableGroups() {
        if (!this.isSuperAdmin) return;
         try {
             const response = await axios.get('/api/auth/groups');
             this.availableGroups = response.data || [];
         } catch (err) {
             console.error('Error fetching groups:', err);
             this.message = 'Не удалось загрузить список групп.';
             this.messageType = 'error';
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
        console.error('Error fetching users:', err);
        this.userListError = 'Не удалось загрузить список пользователей.';
      } finally {
        this.isUserListLoading = false;
      }
    },

    async createUser() {
      this.isLoading = true;
      this.message = '';

      const payload = {
        username: this.newUser.username,
        password: this.newUser.password,
      };

      if (this.isSuperAdmin) {
         payload.role = this.newUser.role;
         payload.groups = this.newUser.groups;
      }

      try {
        const response = await axios.post('/api/auth/users', payload);
        this.message = `Пользователь "${response.data.Username}" (${response.data.Role}) успешно создан.`;
        this.messageType = 'success';
        this.newUser.username = '';
        this.newUser.password = '';
        this.newUser.role = 'User';
        this.newUser.groups = [];
        await this.fetchUsers();
      } catch (err) {
        console.error('Error creating user:', err);
        this.messageType = 'error';
        this.message = err.response?.data?.message || 'Ошибка при создании пользователя.';
      } finally {
        this.isLoading = false;
      }
    },

     openEditGroupsModal(user) {
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
     },
     async updateUserGroups() {
         if (!this.editingUser || !this.isSuperAdmin) return;
         this.isUpdatingGroups = true;
         this.editGroupsError = '';

         if (this.editingUser.Role === 'Admin' && this.editingUserGroups.length === 0) {
             this.editGroupsError = "Администратор должен состоять хотя бы в одной группе!";
             this.isUpdatingGroups = false;
             return;
         }

         try {
             await axios.put(`/api/auth/users/${this.editingUser.Id}/groups`, this.editingUserGroups);
             this.closeEditGroupsModal();
             await this.fetchUsers();
         } catch (err) {
              console.error('Error updating groups:', err);
              this.editGroupsError = err.response?.data?.message || 'Ошибка при обновлении групп.';
         } finally {
             this.isUpdatingGroups = false;
         }
     },

     canDeleteUser(userToDelete) {
         if (userToDelete.Id === this.currentUserId) return false;
         if (this.isSuperAdmin) {
             return userToDelete.Role !== 'SuperAdmin';
         }
         if (this.isAdmin) {
             return userToDelete.Role === 'User' &&
                    userToDelete.Groups.some(ug => this.currentUserGroups.includes(ug));
         }
         return false;
     },
    async deleteUser(userId, username) {
      if (!confirm(`Вы уверены, что хотите удалить пользователя "${username}" (ID: ${userId})?`)) {
        return;
      }
      try {
        await axios.delete(`/api/auth/users/${userId}`);
        this.message = `Пользователь "${username}" удален.`;
        this.messageType = 'success';
        await this.fetchUsers();
      } catch (err) {
        console.error('Error deleting user:', err);
        this.message = err.response?.data?.message || 'Ошибка при удалении пользователя.';
        this.messageType = 'error';
      }
    },

     loadCurrentUser() {
        this.currentUserId = parseInt(localStorage.getItem('userId') || '0');
        this.currentUserRole = localStorage.getItem('userRole');
        try {
            this.currentUserGroups = JSON.parse(localStorage.getItem('userGroups') || '[]');
        } catch { this.currentUserGroups = []; }
     }
  },
  created() {
     this.loadCurrentUser();
     this.fetchUsers();
     if (this.isSuperAdmin) {
       this.fetchAvailableGroups();
     }
  }
};
</script>

<style scoped>
.input-error {
    color: #dc3545;
    font-size: 0.8em;
    display: block;
    margin-top: 4px;
}
.user-list-section { margin-top: 30px; }
.refresh-button { margin-bottom: 15px; }
.user-table { width: 100%; border-collapse: collapse; margin-top: 15px; }
.user-table th, .user-table td { border: 1px solid #ddd; padding: 10px; text-align: left; }
.user-table th { background-color: #f2f2f2; font-weight: bold; }
.user-table td { vertical-align: middle; }
.user-table .action-button {
  background: none; border: none; cursor: pointer; font-size: 1.1rem; padding: 5px;
  margin-right: 5px;
}
.edit-button { color: #ffc107; }
.delete-button { color: #dc3545; }

.checkbox-group {
    display: flex;
    flex-wrap: wrap;
    gap: 15px;
    margin-top: 5px;
    padding: 10px;
    border: 1px solid #eee;
    border-radius: 4px;
    background-color: #f9f9f9;
}
.checkbox-label {
    display: inline-flex;
    align-items: center;
    cursor: pointer;
    margin-right: 10px;
}
.checkbox-label input {
    margin-right: 5px;
    cursor: pointer;
}
.form-group.full-width {
    flex-basis: 100%;
}
.create-user-form small {
    display: block;
    margin-top: 5px;
    font-size: 0.85em;
    color: #6c757d;
}

.modal-overlay {
  position: fixed; top: 0; left: 0; width: 100%; height: 100%;
  background-color: rgba(0, 0, 0, 0.6); display: flex;
  justify-content: center; align-items: center; z-index: 1000;
}
.modal-content {
  background-color: white; padding: 30px; border-radius: 8px;
  min-width: 400px; max-width: 600px; box-shadow: 0 5px 15px rgba(0,0,0,0.3);
}
.modal-content h4 { margin-top: 0; margin-bottom: 20px; }
.modal-actions { margin-top: 25px; display: flex; justify-content: flex-end; gap: 10px; }
.modal-actions button { padding: 10px 20px; border-radius: 4px; cursor: pointer; border: none; }
.save-button { background-color: #28a745; color: white; }
.save-button:disabled { background-color: #ccc; }
.cancel-button { background-color: #6c757d; color: white; }
.cancel-button:disabled { background-color: #ccc; }
.modal-content .error-message { margin-top: 15px; }
</style>