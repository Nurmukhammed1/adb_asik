# Builds the CineMatch site and serves it with nginx. Build context: the frontend/ folder.

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:stable-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/cinematch/browser /usr/share/nginx/html
EXPOSE 80
