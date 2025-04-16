<template>
    <div class="not-found-view">
      <h1>404 - Страница не найдена</h1>
      <p>Извините, страница, которую вы ищете, не существует.</p>
      <router-link :to="{ path: '/' }" class="home-link">Вернуться на главную</router-link>
    </div>
  </template>
  
  <script>
  export default {
    name: 'NotFoundView'
  }
  </script>
  
  <style scoped>
  .not-found-view {
    display: flex;
    flex-direction: column;
    justify-content: center;
    align-items: center;
    min-height: 80vh;
    text-align: center;
    padding: 20px;
  }
  
  h1 {
    color: #dc3545;
    margin-bottom: 15px;
  }
  
  p {
    color: #6c757d;
    margin-bottom: 25px;
    font-size: 1.1rem;
  }
  
  .home-link {
    padding: 10px 20px;
    background-color: #007bff;
    color: white;
    text-decoration: none;
    border-radius: 4px;
    transition: background-color 0.2s ease;
  }
  
  .home-link:hover {
    background-color: #0056b3;
  }
  </style>