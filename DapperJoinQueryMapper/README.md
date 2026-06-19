# DapperJoinQueryMapper

A .NET 10 console application that demonstrates how to use **Dapper's multi-mapping feature** (`QueryAsync<T1, T2, ..., TReturn>`) to map complex SQL JOIN query results into a nested object graph. The example performs a five-table join across `Assignments`, `Tasks`, `Projects`, `Clients`, and `Users` tables, and maps the result into an `AssignmentDto` object with deeply nested related entities (`Task` -> `Project` -> `Client`, plus `User`).

The code is heavily documented to explain the critical rules for Dapper multi-mapping: generic type argument order must match the mapping lambda parameter order, `splitOn` column aliases must match the SELECT column order, and each `splitOn` value marks the boundary where the next object's columns begin. This project is intended as a reference for correctly configuring multi-mapping queries and avoiding common pitfalls like misaligned column ordering or incorrect splitOn values.

## Technologies

- .NET 10
- C#
- Dapper 2.1.66
- Microsoft.Data.SqlClient 6.1.4

## Features

- Multi-table JOIN query with Dapper multi-mapping
- Deeply nested object graph construction (Assignment -> Task -> Project -> Client, plus User)
- Detailed inline documentation on `splitOn`, generic type ordering, and SELECT column alignment

## How to Run

```bash
dotnet run --project DapperJoinQueryMapper
```

The connection string in `Program.cs` points to a local SQL Server instance. Update it to match your database.
