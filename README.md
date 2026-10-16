# TaskAPI

## Запуск PostgreSQL и RabbitMQ

### PostgreSQL
```bash
docker run --name pg-task -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:16
```

### RabbitMQ

```bash
docker run -d --name rabbit-task -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

## Запуск приложениия

Запустите приложение в Visual Studio с помощью кнопки запуска

## Документация

```text
https://localhost:<port>/openapi/v1.json
```

## Проверка функциональности

```powershell
# Создать
Invoke-RestMethod -Method Post -Uri http://localhost:<port>/tasks `
  -ContentType "application/json" -Body '{"title":"Buy milk"}'

# Список
Invoke-RestMethod -Method Get -Uri http://localhost:<port>/tasks

# Завершить
Invoke-RestMethod -Method Put -Uri http://localhost:<port>/tasks/ТВОЙ-ID/complete

# Удалить
Invoke-RestMethod -Method Delete -Uri http://localhost:<port>/tasks/ТВОЙ-ID
```

