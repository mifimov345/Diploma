# Этап сборки
FROM node:18 AS build

WORKDIR /app

# Копируем package.json и package-lock.json из текущей директории (frontend)
COPY package.json package-lock.json ./
RUN npm install

# Копируем оставшиеся файлы
COPY . .
RUN npm run build

# Этап выполнения
FROM nginx:alpine

# Копируем сгенерированный билд из /app/dist в каталог nginx
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
