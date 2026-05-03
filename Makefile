# FjordWatch top-level Makefile
# Orchestration for the local docker compose stack.

SHELL := /usr/bin/env bash
.SHELLFLAGS := -eu -o pipefail -c
.DEFAULT_GOAL := help

COMPOSE_FILE := docker-compose.yml
OBS_COMPOSE_FILE := docker-compose.observability.yml

# Prefer the modern compose plugin (`docker compose`) when present;
# fall back to the legacy standalone binary (`docker-compose`).
COMPOSE_CLI := $(shell docker compose version >/dev/null 2>&1 && echo "docker compose" || echo "docker-compose")
COMPOSE := $(COMPOSE_CLI) -f $(COMPOSE_FILE)
COMPOSE_OBS := $(COMPOSE_CLI) -f $(COMPOSE_FILE) -f $(OBS_COMPOSE_FILE)

ENV_FILE := .env
ENV_EXAMPLE := .env.example

.PHONY: help up up-obs down logs ps build pull restart test lint format seed clean reset env validate

help:  ## List available targets
	@awk 'BEGIN{FS=":.*##"} /^[a-zA-Z_-]+:.*##/ {printf "  %-14s %s\n", $$1, $$2}' $(MAKEFILE_LIST)

env:  ## Create .env from .env.example if missing
	@if [ ! -f $(ENV_FILE) ]; then cp $(ENV_EXAMPLE) $(ENV_FILE); echo "Created $(ENV_FILE) from $(ENV_EXAMPLE)"; else echo "$(ENV_FILE) already exists"; fi

validate:  ## Validate compose files
	$(COMPOSE) config --quiet
	$(COMPOSE_OBS) config --quiet
	@echo "compose files OK"

up: env  ## Start the local stack
	$(COMPOSE) up -d --remove-orphans

up-obs: env  ## Start the local stack with the observability profile
	$(COMPOSE_OBS) up -d --remove-orphans

down:  ## Stop the local stack
	$(COMPOSE) down --remove-orphans

ps:  ## Show running services
	$(COMPOSE) ps

logs:  ## Tail logs
	$(COMPOSE) logs -f --tail=100

build:  ## Build all service images
	$(COMPOSE) build

pull:  ## Pull base images
	$(COMPOSE) pull

restart:  ## Restart the stack
	$(MAKE) down
	$(MAKE) up

test:  ## Run all language test suites (placeholder until services exist)
	@echo "test: no service test suites wired yet (phase 0)"

lint:  ## Run all linters (placeholder until services exist)
	@echo "lint: no service linters wired yet (phase 0)"

format:  ## Run all formatters (placeholder until services exist)
	@echo "format: no service formatters wired yet (phase 0)"

seed:  ## Seed databases / object storage with fixtures (placeholder)
	@echo "seed: no fixtures yet (phase 0)"

clean:  ## Remove volumes and orphaned containers
	$(COMPOSE) down --volumes --remove-orphans

reset: clean up  ## Full reset: clean then bring back up
