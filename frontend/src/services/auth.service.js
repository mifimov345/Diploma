import axios from 'axios';

const API_URL = '/auth';

class AuthService {
    login(username, password) {
        return axios.post(API_URL + '/login', { username, password })
            .then(response => {
                if (response.data.Token) {
                    localStorage.setItem('jwtToken', response.data.Token);
                    localStorage.setItem('userRole', response.data.Role);
                    localStorage.setItem('userGroup', response.data.Group);
                    localStorage.setItem('username', response.data.Username);
                }
                return response.data;
            });
    }

    logout() {
        localStorage.removeItem('jwtToken');
        localStorage.removeItem('userRole');
        localStorage.removeItem('userGroup');
        localStorage.removeItem('username');
    }


    createUser(userData) {
         return axios.post(API_URL + '/create-user', userData);
    }
}

export default new AuthService();