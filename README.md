# OrderAssistant.Api

ASP.NET Core minimal API over PostgreSQL (EF Core). Orders, order lines, customers, products,
warehouses and inventory, seeded with generated data in Development.

## Run

1. PostgreSQL running locally, connection string in user secrets under `ConnectionStrings:Oms`.
2. `dotnet run --project OrderAssistant.Api --launch-profile http` (http://localhost:5210).
   In Development, migrations are applied and seed data is added on startup.

## Endpoints

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/api/orders?q=&status=&page=1&pageSize=20` | `{ items, page, pageSize, totalCount }`; `q` matches order number or customer, case-insensitive; `status` any casing; 400 on bad params |
| GET | `/api/orders/{id}` | Order with lines, or 404 |
| GET | `/api/inventory/{sku}` | Stock per warehouse, or 404 for an unknown SKU |
| GET | `/health` | Includes a database check |
| GET | `/openapi/v1.json` | Development only |

Errors are RFC 7807 problem details. Try them from `OrderAssistant.Api/OrderAssistant.Api.http`.

## Test

`dotnet test` (needs Docker: each run starts a throwaway PostgreSQL container, never the dev database).

## Migrations

`dotnet tool restore`, then `dotnet ef migrations add <Name> --project OrderAssistant.Api`.
