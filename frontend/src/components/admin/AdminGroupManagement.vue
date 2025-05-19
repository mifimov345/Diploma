<template>
  <div class="admin-group-management">
    <h2>Управление группами</h2>

    <section class="create-group-section">
      <h3>Создать новую группу</h3>
      <form @submit.prevent="createGroup" class="create-group-form">
        <div class="form-group">
          <label for="new-group-name">Название группы:</label>
          <input
            id="new-group-name"
            v-model.trim="newGroupName"
            required
            :disabled="isLoading"
            placeholder="Например, Marketing_Team"
            maxlength="50"
            pattern="^[a-zA-Z0-9_.\-]+$"
            title="Имя группы может содержать буквы, цифры, _, -, ."
          />
          <small>Разрешены буквы, цифры, _, -, . (макс. 50 симв.)</small>
        </div>
        <button type="submit" :disabled="isLoading || !newGroupName || !isGroupNameValid" class="create-button">
          <span v-if="isLoading">Создание...</span>
          <span v-else>Создать группу</span>
        </button>
      </form>
      <div v-if="message" :class="['message', messageType]">{{ message }}</div>
    </section>

    <section class="group-list-section">
      <h3>Существующие группы</h3>
       <button @click="fetchGroups" :disabled="isListLoading" class="refresh-button" title="Обновить список групп">
           <span v-if="isListLoading">Обновление...</span>
           <span v-else>Обновить список</span>
       </button>
       <div v-if="isListLoading && groups.length === 0" class="loading-indicator">Загрузка списка...</div>
       <div v-if="listError" class="error-message">{{ listError }}</div>

       <ul v-if="groups.length > 0" class="group-list">
        <li v-for="groupName in sortedGroups" :key="groupName" class="group-item">
          <span class="group-name-text">{{ groupName }}</span>
          <button
             v-if="groupName.toLowerCase() !== 'system'"
             @click="deleteGroup(groupName)"
             :disabled="isDeleting === groupName"
             class="action-button delete-button"
             title="Удалить группу"
          >
             <span v-if="isDeleting === groupName">...</span>
             <span v-else>🗑️</span>
          </button>
           <span v-else class="system-group-info" title="Системная группа, не может быть удалена">(системная)</span>
        </li>
      </ul>
      <div v-else-if="!isListLoading && !listError && groups.length === 0" class="no-items-message">
           <p>Группы не найдены. Создайте новую группу выше.</p>
       </div>
    </section>
  </div>
</template>

<script>
import axios from 'axios';

export default {
  name: 'AdminGroupManagement',
  data() {
    return {
      newGroupName: '',
      isLoading: false,
      message: '',
      messageType: 'success',
      groups: [],
      isListLoading: false,
      listError: '',
      isDeleting: null,
      currentUserRole: null,
    };
  },
  computed: {
      isSuperAdmin() {
         return this.currentUserRole === 'SuperAdmin';
     },
      isGroupNameValid() {
        if (this.newGroupName.toLowerCase() === 'system') return false;
          const pattern = /^[a-zA-Z0-9_.-]+$/; // Оставляем ваш паттерн
          return pattern.test(this.newGroupName) && this.newGroupName.length <= 50;
      },
      sortedGroups() {
         const filtered = this.isSuperAdmin
             ? this.groups
             : this.groups.filter(g => g.toLowerCase() !== 'system');
         return [...filtered].sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()));
      }
  },
  methods: {
    loadCurrentUser() {
        this.currentUserRole = localStorage.getItem('userRole');
    },
    async fetchGroups() {
      this.isListLoading = true;
      this.listError = '';
      try {
        const response = await axios.get('/api/auth/groups');
        //console.log('>>> AdminGroupManagement fetchGroups RAW RESPONSE DATA:', response.data);

        if (Array.isArray(response.data) && response.data.every(item => typeof item === 'string')) {
             this.groups = response.data;
             //console.log('>>> AdminGroupManagement groups assigned (count):', this.groups.length);
             if (this.groups.length === 0 && this.isSuperAdmin) {
                 this.listError = '';
             }
        } else {
             //console.error('Received group data is not an array of strings:', response.data);
             this.listError = 'Получены некорректные данные списка групп от сервера.';
             this.groups = [];
        }
      } catch (err) {
        //console.error("Error fetching groups:", err);
        this.listError = err.response?.data?.message || 'Не удалось загрузить список групп. Проверьте соединение или права доступа.';
        this.groups = [];
      } finally {
        this.isListLoading = false;
      }
    },
    async createGroup() {
      if (!this.newGroupName || this.isLoading || !this.isGroupNameValid) return;

      this.isLoading = true;
      this.message = '';
      this.listError = '';
      try {
        const groupNameToCreate = this.newGroupName.trim();
        const response = await axios.post('/api/auth/groups', { groupName: groupNameToCreate });
        this.message = response.data.message || `Группа "${groupNameToCreate}" успешно создана.`;
        this.messageType = 'success';
        this.newGroupName = '';
        try {
            const currentGroups = JSON.parse(localStorage.getItem('userGroups') || '[]');
            if (!currentGroups.includes(groupNameToCreate)) {
                currentGroups.push(groupNameToCreate);
                localStorage.setItem('userGroups', JSON.stringify(currentGroups));
                console.log('Updated userGroups in localStorage:', currentGroups);
            }
        } catch (e) {
            console.error("Failed to update userGroups in localStorage", e);
        }
        await this.fetchGroups();
      } catch (err) {
        //console.error("Error creating group:", err);
        this.message = err.response?.data?.message || 'Ошибка при создании группы.';
        this.messageType = 'error';
        if (err.response?.status === 403) {
          this.message = 'У вас нет прав на создание групп.';
       }
      } finally {
        this.isLoading = false;
      }
    },
    async deleteGroup(groupName) {
       if (this.isDeleting || groupName.toLowerCase() === 'system') return;
       if (!this.isSuperAdmin) {
          alert('Только SuperAdmin может удалять группы.');
          return;
      }
       if (groupName.toLowerCase() === 'system') {
           alert('Системную группу нельзя удалить.');
           return;
       }
       if (!confirm(`Вы уверены, что хотите удалить группу "${groupName}"? Это действие удалит группу у всех пользователей, которые в ней состоят.`)) {
           return;
       }
      this.isDeleting = groupName;
      this.message = '';
      this.listError = '';
      try {
        await axios.delete(`/api/auth/groups/${encodeURIComponent(groupName)}`);
        this.groups = this.groups.filter(g => g !== groupName);
      } catch (err) {
        //console.error(`Error deleting group ${groupName}:`, err);
        this.listError = `Ошибка при удалении группы "${groupName}": ${err.response?.data?.message || err.message}`;
        if (err.response?.status === 403) {
          this.listError = 'У вас нет прав на удаление групп.';
       }
      } finally {
        this.isDeleting = null;
      }
    },
  },
  mounted() {
    this.loadCurrentUser();
    this.fetchGroups();
  }
};
</script>

<style scoped>
.admin-group-management {
    padding: 25px;
    background-color: #fff;
    border-radius: 8px;
    box-shadow: 0 2px 10px rgba(0,0,0,0.07);
}

h2 {
    margin-top: 0;
    margin-bottom: 25px;
    color: #333;
    border-bottom: 1px solid #eee;
    padding-bottom: 10px;
}
h3 {
    margin-top: 0;
    margin-bottom: 20px;
    color: #444;
    font-size: 1.2rem;
}

.create-group-section, .group-list-section {
    margin-bottom: 35px;
    padding: 20px;
    border: 1px solid #e9ecef;
    border-radius: 6px;
    background-color: #fdfdfd;
}
.group-list-section {
    margin-top: 30px;
}

/* Форма */
.create-group-form {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    gap: 15px;
    margin-bottom: 15px;
}
.form-group {
    flex-grow: 1;
    min-width: 250px;
}
label {
    display: block;
    margin-bottom: 6px;
    font-weight: 600;
    color: #555;
    font-size: 0.9rem;
}
input[type="text"] {
    width: 100%;
    padding: 10px 12px;
    border: 1px solid #ccc;
    border-radius: 4px;
    box-sizing: border-box;
    font-size: 1rem;
}
input[type="text"]:focus {
    border-color: #007bff;
    outline: none;
    box-shadow: 0 0 0 2px rgba(0, 123, 255, 0.2);
}
input:invalid {
  border-color: #dc3545;
}
.form-group small {
    font-size: 0.8em;
    color: #6c757d;
    margin-top: 4px;
    display: block;
}
.create-button {
    padding: 10px 20px;
    background-color: #28a745;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 1rem;
    transition: background-color 0.2s ease;
    height: 42px;
    white-space: nowrap;
    align-self: flex-end;
}
.create-button:hover:not(:disabled) { background-color: #218838; }
.create-button:disabled { background-color: #cccccc; cursor: not-allowed; }

.refresh-button {
    margin-bottom: 15px;
    padding: 8px 15px;
    background-color: #007bff;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    transition: background-color 0.2s;
}
.refresh-button:hover:not(:disabled) { background-color: #0056b3; }
.refresh-button:disabled { background-color: #cccccc; cursor: not-allowed; }

.loading-indicator {
    text-align: center; padding: 15px; color: #6c757d;
}
.no-items-message {
    text-align: center; padding: 15px; background-color: #e9ecef; border-radius: 4px; color: #495057;
}

.group-list {
    list-style: none;
    padding: 0;
    margin: 0;
    max-width: 600px;
}
.group-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 15px;
    border: 1px solid #eee;
    border-radius: 4px;
    margin-bottom: 8px;
    background-color: #fff;
    transition: background-color 0.15s ease;
}
.group-item:nth-child(odd) { background-color: #f9f9f9; }
.group-item:hover { background-color: #f1f1f1; }

.group-name-text {
    font-weight: 500;
    color: #343a40;
    word-break: break-all;
    margin-right: 10px;
}
.action-button {
    background: none;
    border: none;
    cursor: pointer;
    font-size: 1.1rem;
    padding: 5px;
    margin-left: 10px;
    color: #dc3545;
    transition: color 0.2s ease, transform 0.1s ease;
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
}
.action-button:hover:not(:disabled) { color: #c82333; transform: scale(1.1); }
.action-button:disabled { opacity: 0.5; cursor: not-allowed; }

.system-group-info {
    font-size: 0.85em;
    color: #6c757d;
    font-style: italic;
    margin-left: 10px;
    flex-shrink: 0;
}

.message, .error-message {
    padding: 12px 15px;
    margin-top: 15px;
    border-radius: 4px;
    border: 1px solid transparent;
    font-size: 0.95rem;
}
.message.success { background-color: #d4edda; color: #155724; border-color: #c3e6cb; }
.message.error { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
.error-message { background-color: #f8d7da; color: #721c24; border-color: #f5c6cb; }
</style>