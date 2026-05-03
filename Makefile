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

test: test-rust test-dotnet test-python  ## Run all language test suites

test-rust:  ## Run Rust test suite for ais-ingestion
	cd services/ais-ingestion && cargo test --workspace

test-dotnet:  ## Run .NET xUnit tests for core-api
	cd services/core-api && dotnet test

test-python:  ## Run pytest for the anomaly-detection service
	cd services/anomaly-detection && pytest -q

lint: lint-rust lint-dotnet lint-python  ## Run all linters

lint-rust:  ## Run cargo fmt --check + cargo clippy -D warnings
	cd services/ais-ingestion && cargo fmt --all -- --check
	cd services/ais-ingestion && cargo clippy --workspace --all-targets -- -D warnings

lint-dotnet:  ## Run dotnet format --verify-no-changes
	cd services/core-api && dotnet format --verify-no-changes
	cd services/web && dotnet format --verify-no-changes

lint-python:  ## Run ruff + mypy on Python services
	cd services/anomaly-detection && ruff check . && ruff format --check . && mypy src

format: format-rust format-dotnet format-python  ## Run all formatters

format-rust:  ## Run cargo fmt
	cd services/ais-ingestion && cargo fmt --all

format-dotnet:  ## Run dotnet format
	cd services/core-api && dotnet format
	cd services/web && dotnet format

format-python:  ## Run ruff format on Python services
	cd services/anomaly-detection && ruff format .

migrate:  ## Run database migrations once (Flyway)
	$(COMPOSE) run --rm db-migrate

ais-replay:  ## Start ais-ingestion replaying the bundled NMEA fixture
	AIS_REPLAY_FILE=/fixtures/sample.nmea $(COMPOSE) up -d --build ais-ingestion

seed:  ## Seed databases / object storage with fixtures (placeholder)
	@echo "seed: no fixtures yet (phase 0)"

clean:  ## Remove volumes and orphaned containers
	$(COMPOSE) down --volumes --remove-orphans

reset: clean up  ## Full reset: clean then bring back up
