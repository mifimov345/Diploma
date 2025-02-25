# Используем официальный Node.js образ
FROM node:18 AS build

WORKDIR /app

# Копируем package.json и устанавливаем зависимости
COPY package.json package-lock.json ./
RUN npm install

# Копируем все файлы фронтенда и создаем билд
COPY . .
RUN npm run build

# Настроим сервер для обслуживания фронтенда
FROM nginx:alpine

# Копируем билд в директорию nginx
COPY --from=build /app/dist /usr/share/nginx/html

# Открываем порт для nginx
EXPOSE 80

# Запускаем nginx
CMD ["nginx", "-g", "daemon off;"]
