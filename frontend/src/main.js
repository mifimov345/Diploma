import { createApp } from 'vue';
import App from './App.vue';
import router from './router';
import axios from 'axios';


axios.defaults.baseURL = process.env.VUE_APP_API_BASE_URL || '';
//console.log('Axios Base URL configured:', axios.defaults.baseURL);

axios.interceptors.request.use(config => {
  const token = localStorage.getItem('jwtToken');
  //console.log('INTERCEPTOR - Token from localStorage:', token);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
    //console.log('INTERCEPTOR - Authorization header SET');
  } else{
    //console.log('INTERCEPTOR - Authorization header NOT SET');
  }
  return config;
}, error => {
  return Promise.reject(error);
});

axios.interceptors.response.use(response => {
  //console.log(`<<< AXIOS INTERCEPTOR (Response): Success for ${response.config.url} Status: ${response.status}`);
  return response;
}, error => {
  //console.error(`<<< AXIOS INTERCEPTOR (Response): Error for ${error.config?.url}`);
  if (error.response) {
      //console.error(`<<< AXIOS INTERCEPTOR (Response): Error Status: ${error.response.status}`);
      if (error.response.status === 401) {
        //console.error("<<< AXIOS INTERCEPTOR (Response): Status is 401. Logging out.");
        // Очищаем localStorage
        localStorage.removeItem('jwtToken');
        localStorage.removeItem('userRole');
        localStorage.removeItem('userGroups');
        localStorage.removeItem('username');

        if (router.currentRoute.value.name !== 'Login') {
          //console.error("<<< AXIOS INTERCEPTOR (Response): Redirecting to Login.");
          router.push({ name: 'Login' });
        } else {
          //console.error("<<< AXIOS INTERCEPTOR (Response): Already on Login page, not redirecting.");
        }
      } else {
         //console.log(`<<< AXIOS INTERCEPTOR (Response): Error status ${error.response.status} (NOT 401). Logout NOT triggered.`);
      }
  } else {
       //console.error("<<< AXIOS INTERCEPTOR (Response): Network or other request error. Logout NOT triggered.");
  }
  return Promise.reject(error);
});

const app = createApp(App);

app.use(router);


app.mount('#app');