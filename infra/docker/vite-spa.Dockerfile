FROM node:22.22.3-alpine AS build
WORKDIR /src

ARG APP_FILTER
ARG APP_DIR
RUN corepack enable
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./frontend/
COPY frontend ./frontend
RUN pnpm -C frontend install --frozen-lockfile
RUN pnpm -C frontend --filter "$APP_FILTER" build

FROM nginx:1.27-alpine AS final
ARG APP_DIR
COPY infra/docker/nginx-spa.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/$APP_DIR/dist /usr/share/nginx/html
