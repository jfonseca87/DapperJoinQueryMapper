using Dapper;
using Microsoft.Data.SqlClient;

string connectionString = "Server=localhost;Database=TaskManagementDB;User Id=sa;Password=Abc.123456;MultipleActiveResultSets=true;TrustServerCertificate=True";

using var connection = new SqlConnection(connectionString);

// IMPORTANT:
// - Dapper multi-mapping requires the SELECT column order to align with how the results are split into objects.
// - The sequence of mapped types in QueryAsync<T1, T2, ..., TReturn> -> (t1, t2, ...) => return must match:
//     1) The order of the generic type arguments (T1, T2, ...), and
//     2) The order of the parameters in the mapping lambda (t1, t2, ...).
// - The splitOn string lists the column names that mark the beginning of each subsequent object
//   in the same order as the second..N-th mapped types. Each name in splitOn must match an alias
//   in the SELECT that appears *at the point where that object's columns begin*.
// - Dapper's default splitOn value is "Id". If you alias your id columns (recommended when joining),
//   you must provide splitOn explicitly with those alias names, in the same order as the types.
//
// Concrete mapping in this file:
// Generic types: <AssignmentDto, TaskDto, ProjectDto, ClientDto, UserDto, AssignmentDto>
// Mapping lambda parameters: (assignment, task, project, client, user) => { ... }
// splitOn: "TaskId, ProjectId, ClientId, UserId"
// => The SELECT must emit the AssignmentDto columns first, then a column named "TaskId" (the first column
//    of TaskDto), then a column named "ProjectId" (first column of ProjectDto), etc.
// => If the SELECT order or alias names change, the mapping will be wrong (fields will map to wrong objects or null).

string sql = @"
            -- The SELECT is intentionally ordered. The first set of columns are for AssignmentDto,
            -- followed by TaskDto (starting at the column aliased as TaskId),
            -- then ProjectDto (starting at ProjectId),
            -- then ClientDto (starting at ClientId),
            -- then UserDto (starting at UserId).
            -- These aliases MUST match the values provided in splitOn, and must appear in this same sequence.
            SELECT 
                    a.Id AS AssignmentId,                    -- AssignmentDto's identifying/first column(s)
                    a.StartDate AS AssignmentStartDate,
                    a.EndDate AS AssignmentEndDate,
                    a.EstimatedHours AS AssignmentEstimatedHours,
                    a.ActualHours AS AssignmentActualHours,
                    a.AssignmentStatus,
                    -- Begin TaskDto columns: the first column here is aliased 'TaskId' which corresponds to the first split
                    b.Id AS TaskId,
                    b.Name AS TaskName,
                    b.Description AS TaskDescription,
                    b.Status AS TaskStatus,
                    -- Begin ProjectDto columns: first column aliased 'ProjectId' - second split
                    c.Id AS ProjectId,
                    c.Name AS ProjectName,
                    c.Status AS ProjectStatus,
                    -- Begin ClientDto columns: first column aliased 'ClientId' - third split
                    d.Id AS ClientId,
                    d.Name AS ClientName,
                    d.Email AS ClientEmail,
                    -- Begin UserDto columns: first column aliased 'UserId' - fourth split
                    e.Id AS UserId,
                    e.Name AS UserName,
                    e.Email AS UserEmail
            FROM Assignments a
            INNER JOIN Tasks b ON a.TaskId = b.Id
            INNER JOIN Projects c ON b.ProjectId = c.Id
            INNER JOIN Clients d ON c.ClientId = d.Id
            INNER JOIN Users e ON a.UserId = e.Id
        ";

/*
Detailed notes on QueryAsync configuration and common pitfalls:

1) Generic type order must match mapping lambda order:
   QueryAsync<AssignmentDto, TaskDto, ProjectDto, ClientDto, UserDto, AssignmentDto>(
       sql,
       (assignment, task, project, client, user) => { ... }
       ...
   );
   - The types after the first are the additional objects found in the row.
   - The last generic type is the return type of the mapping function (AssignmentDto here),
     so we repeat AssignmentDto at the end.

2) splitOn:
   - splitOn lists the column names that signal where Dapper should start mapping the next object.
     It MUST be in the same order as the second..N-th type parameters.
   - For N types total, you supply (N - 1) splitOn values.
   - If your SELECT aliases those id/start columns as 'TaskId', 'ProjectId', etc., splitOn must use those exact names.
   - Example: splitOn: ""TaskId, ProjectId, ClientId, UserId""
     means:
       - At column named TaskId, start mapping TaskDto,
       - At column named ProjectId, start mapping ProjectDto,
       - At column named ClientId, start mapping ClientDto,
       - At column named UserId, start mapping UserDto.
   - NOTE: Dapper's default splitOn is 'Id' if you omit it. If you alias your id columns (recommended),
     you must explicitly provide splitOn.

3) SELECT ordering:
   - The SELECT should place the columns for the primary object (AssignmentDto) first.
   - Immediately after those columns, place the first column used in the first split (TaskId).
   - That first column for each split should be the earliest column for that object's projection.
   - Changing the SELECT order or alias names without updating splitOn or the generic type order will silently break mapping.

4) Lambda parameter order:
   - The mapping lambda signature must match the type sequence: (AssignmentDto assignment, TaskDto task, ProjectDto project, ClientDto client, UserDto user)
   - If you swap parameters in the lambda but not the generic types, mapping will not align and fields may be null or incorrect.

5) When two objects share property names:
   - Dapper maps columns to properties by name within the boundaries determined by splitOn.
   - Ensure property names in DTOs match chosen column aliases to get correct mapping.

6) Example pitfalls:
   - If you alias Task.Id as 'Id' and Projects.Id as 'Id' and omit splitOn, Dapper will split on the first 'Id' it finds,
     which may not produce the intended object boundaries.
   - If you specify splitOn values in the wrong order (e.g., "ProjectId,TaskId,...") mapping will be mixed up.

7) Testing & debugging tips:
   - Temporarily SELECT only Id columns with aliases in the same order to verify split boundaries.
   - Log the raw query and the first row of column names to verify the order and aliasing match expectations.
*/
IEnumerable<AssignmentDto> assignments = await connection.QueryAsync<AssignmentDto, TaskDto, ProjectDto, ClientDto, UserDto, AssignmentDto>(
    sql,
    (assignment, task, project, client, user) =>
    {
        // Mapping lambda - parameter order corresponds to generic type order above.
        // Here we attach related objects to each other in the expected object graph.
        // If any of the above ordering rules are violated, these objects may be null or contain incorrect data.
        project.Client = client;
        task.Project = project;
        assignment.Task = task;
        assignment.User = user;
        return assignment;
    },
    // splitOn must list the first column name (alias) that starts each subsequent object.
    // The order here MUST match:
    //   1) the order of the second..N-th generic type arguments, and
    //   2) the order those aliased columns appear in the SELECT above.
    // Provide exactly (number of types - 1) values.
    splitOn: "TaskId, ProjectId, ClientId, UserId");

Console.ReadLine();


/// <summary>
/// Represents a client.
/// </summary>
public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; }

    // Navigation property
    public ICollection<Project>? Projects { get; set; }
}

/// <summary>
/// Represents a project belonging to a client.
/// </summary>
public class Project
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EstimatedEndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Represents a user (employee, contractor, etc.).
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public DateTime RegistrationDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Represents a task within a project.
/// </summary>
public class Task
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreationDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Represents an assignment of a user to a task.
/// </summary>
public class Assignment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int UserId { get; set; }
    public DateTime AssignmentDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }
    public string AssignmentStatus { get; set; } = string.Empty;
    public string? Comments { get; set; }
}

public class AssignmentDetailDto
{
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? ProjectStatus { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string? TaskDescription { get; set; }
    public string? TaskStatus { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public DateTime? AssignmentStartDate { get; set; }
    public DateTime? AssignmentEndDate { get; set; }
    public decimal? AssignmentEstimatedHours { get; set; }
    public decimal? AssignmentActualHours { get; set; }
    public string? AssignmentStatus { get; set; }
}

/// <summary>
/// Client data from the assignment details query.
/// </summary>
public class ClientDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
}

/// <summary>
/// Project data from the assignment details query.
/// </summary>
public class ProjectDto
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? ProjectStatus { get; set; }

    public ClientDto? Client { get; set; }
}

/// <summary>
/// Task data from the assignment details query.
/// </summary>
public class TaskDto
{
    public int TaskId { get; set; }
    public string TaskName { get; set; } = string.Empty;
    public string? TaskDescription { get; set; }
    public string? TaskStatus { get; set; }

    public ProjectDto? Project { get; set; }
}

/// <summary>
/// User data from the assignment details query.
/// </summary>
public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
}

/// <summary>
/// Assignment data from the assignment details query.
/// </summary>
public class AssignmentDto
{
    public int AssignmentId { get; set; }
    public DateTime? AssignmentStartDate { get; set; }
    public DateTime? AssignmentEndDate { get; set; }
    public decimal? AssignmentEstimatedHours { get; set; }
    public decimal? AssignmentActualHours { get; set; }
    public string? AssignmentStatus { get; set; }

    public TaskDto? Task { get; set; }
    public UserDto? User { get; set; }
}
