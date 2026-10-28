export IMAGE_NAME ?= localhost/backend:latest
SECRET_FILE       ?= secret.yaml
KUBE_FILE         ?= backend-pod.yaml
POD_NAME          ?= chaoslab-stack

.PHONY: help build up down restart ps logs logs-backend logs-db clean-data secret-init dev-fe dev-be

help:
	@echo "Available commands:"
	@echo "  make build         - Build the backend container image (IMAGE_NAME=$(IMAGE_NAME))"
	@echo "  make up            - Start the pod stack with secrets and dynamic image substitution"
	@echo "  make down          - Stop and remove the pod (preserves database data)"
	@echo "  make restart       - Restart the pod stack"
	@echo "  make dev-fe        - Run frontend"
	@echo "  make dev-be        - Run backend"
	@echo "  make ps            - Check status of running pods and containers"
	@echo "  make logs          - Follow logs from all containers in the pod"
	@echo "  make logs-backend  - Follow backend container logs"
	@echo "  make logs-db       - Follow postgres container logs"
	@echo "  make clean-data    - Stop pod and delete persistent volume (DESTRUCTIVE)"
	@echo "  make secret-init   - Initialize $(SECRET_FILE) from $(SECRET_FILE).example if missing"

secret-init:
	@if [ ! -f $(SECRET_FILE) ]; then \
		cp $(SECRET_FILE).example $(SECRET_FILE); \
		echo "Created $(SECRET_FILE) from $(SECRET_FILE).example"; \
	fi

build:
	podman build -t $(IMAGE_NAME) Backend/

up: secret-init
	(cat $(SECRET_FILE) && echo "---" && envsubst '$${IMAGE_NAME}' < $(KUBE_FILE)) | podman play kube -

down:
	@if [ -f $(SECRET_FILE) ]; then \
		(cat $(SECRET_FILE) && echo "---" && envsubst '$${IMAGE_NAME}' < $(KUBE_FILE)) | podman play kube --down - ; \
	else \
		podman pod stop $(POD_NAME) 2>/dev/null || true; \
		podman pod rm $(POD_NAME) 2>/dev/null || true; \
	fi

restart: down up

dev-fe: 
	cd Frontend && pnpm run dev

dev-be:
	cd Backend && dotnet run

ps:
	podman ps --pod

logs:
	podman pod logs -f --names $(POD_NAME)

logs-backend:
	podman logs -f $(POD_NAME)-backend

logs-db:
	podman logs -f $(POD_NAME)-postgres

clean-data: down
	podman volume rm -f chaoslab-postgres-data
