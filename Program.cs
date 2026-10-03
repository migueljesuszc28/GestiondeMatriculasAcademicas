using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuración de servicios (Swagger / OpenAPI, Explorador de endpoints y CORS)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// 2. Configuración del Pipeline HTTP (Swagger habilitado para desarrollo y producción en Render)
app.UseSwagger();
app.UseSwaggerUI(); // Interfaz visual en /swagger

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ====================================================================
// 3. COLECCIONES DE DATOS INICIALES EN MEMORIA
// ====================================================================

List<Asignatura> ObtenerAsignaturasIniciales() => new()
{
    new Asignatura(101, "Estructuras de Datos", "SIS-101", 3),
    new Asignatura(102, "Programación Web", "SIS-102", 4),
    new Asignatura(103, "Bases de Datos I", "SIS-103", 3)
};

List<Estudiante> ObtenerEstudiantesIniciales() => new()
{
    new Estudiante(1, "Ana Gómez", "Ingeniería de Sistemas"),
    new Estudiante(2, "Carlos López", "Ingeniería de Sistemas"),
    new Estudiante(3, "María Rodríguez", "Ingeniería de Software"),
    new Estudiante(4, "Juan Martínez", "Ingeniería de Sistemas")
};

var asignaturas = ObtenerAsignaturasIniciales();
var matriculas = new List<Matricula>();
var estudiantes = ObtenerEstudiantesIniciales();

// ====================================================================
// 4. ENDPOINTS ESPECIFICADOS EN EL TALLER
// ====================================================================

// --------------------------------------------------------------------
// Endpoint 1: GET /api/asignaturas
// Regla 3: Oferta Académica Disponible
// Consultar únicamente las asignaturas ofertadas/activas (Activa == true).
// --------------------------------------------------------------------
app.MapGet("/api/asignaturas", () =>
{
    var disponibles = asignaturas.Where(a => a.Activa).ToList();
    return Results.Ok(disponibles);
})
.WithName("ObtenerAsignaturasActivas")
.WithTags("Asignaturas")
.WithSummary("Consulta únicamente las asignaturas ofertadas/activas (Activa == true).");

// --------------------------------------------------------------------
// Endpoint 2: POST /api/asignaturas
// Registrar una nueva asignatura académica.
// --------------------------------------------------------------------
app.MapPost("/api/asignaturas", (Asignatura nuevaAsignatura) =>
{
    if (string.IsNullOrWhiteSpace(nuevaAsignatura.Nombre))
    {
        return Results.BadRequest(new { mensaje = "El nombre de la asignatura es obligatorio." });
    }

    if (string.IsNullOrWhiteSpace(nuevaAsignatura.Codigo))
    {
        return Results.BadRequest(new { mensaje = "El código de la asignatura es obligatorio." });
    }

    if (nuevaAsignatura.Creditos <= 0)
    {
        return Results.BadRequest(new { mensaje = "Los créditos deben ser mayores a 0." });
    }

    if (asignaturas.Any(a => a.Id == nuevaAsignatura.Id))
    {
        return Results.BadRequest(new { mensaje = $"Ya existe una asignatura registrada con el ID {nuevaAsignatura.Id}." });
    }

    if (asignaturas.Any(a => string.Equals(a.Codigo, nuevaAsignatura.Codigo, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest(new { mensaje = $"Ya existe una asignatura con el código '{nuevaAsignatura.Codigo}'." });
    }

    int idGenerado = nuevaAsignatura.Id > 0 ? nuevaAsignatura.Id : (asignaturas.Any() ? asignaturas.Max(a => a.Id) + 1 : 101);
    var asignaturaAGuardar = nuevaAsignatura with { Id = idGenerado, Activa = true };

    asignaturas.Add(asignaturaAGuardar);
    return Results.Created($"/api/asignaturas/{asignaturaAGuardar.Id}", asignaturaAGuardar);
})
.WithName("RegistrarAsignatura")
.WithTags("Asignaturas")
.WithSummary("Registra una nueva asignatura académica.");

// --------------------------------------------------------------------
// Endpoint 3: DELETE /api/asignaturas/{id}
// Regla 1: Control de Eliminación de Asignaturas (Borrado Lógico)
// - Si la asignatura NO tiene matrículas asociadas en ningún periodo,
//   se elimina físicamente de la lista (asignaturas.Remove).
// - Si la asignatura YA cuenta con estudiantes matriculados, el sistema
//   no la borra. En su lugar, aplica borrado lógico cambiando su estado
//   a Activa = false para preservar el historial académico.
// --------------------------------------------------------------------
app.MapDelete("/api/asignaturas/{id:int}", (int id) =>
{
    var asignatura = asignaturas.FirstOrDefault(a => a.Id == id);
    if (asignatura is null)
    {
        return Results.NotFound(new { mensaje = $"No se encontró la asignatura con ID {id}." });
    }

    bool tieneMatriculas = matriculas.Any(m => m.AsignaturaId == id);

    if (!tieneMatriculas)
    {
        // Eliminación física
        asignaturas.Remove(asignatura);
        return Results.NoContent();
    }
    else
    {
        // Borrado lógico (Soft Delete)
        var indice = asignaturas.IndexOf(asignatura);
        var asignaturaInactivada = asignatura with { Activa = false };
        asignaturas[indice] = asignaturaInactivada;

        return Results.Ok(new
        {
            mensaje = "La asignatura cuenta con matrículas asociadas. Se aplicó borrado lógico (Activa = false) para preservar el historial académico.",
            asignatura = asignaturaInactivada
        });
    }
})
.WithName("EliminarOInactivarAsignatura")
.WithTags("Asignaturas")
.WithSummary("Elimina físicamente la asignatura si no tiene matrículas, o aplica borrado lógico si cuenta con estudiantes inscritos.");

// --------------------------------------------------------------------
// Endpoint 4: POST /api/matriculas
// Regla 2: Validaciones del Proceso de Matrícula
// - Verificar la existencia del estudiante en el sistema.
// - Validar que la asignatura exista Y se encuentre Activa == true.
//   Si está inactiva, retornar un error HTTP 400 (Bad Request).
// - Control de duplicados por periodo: Un estudiante no puede matricular
//   la misma asignatura dos veces dentro del mismo año y periodo lectivo.
// --------------------------------------------------------------------
app.MapPost("/api/matriculas", (Matricula nuevaMatricula) =>
{
    // 1. Verificar la existencia del estudiante en el sistema
    var estudiante = estudiantes.FirstOrDefault(e => e.Id == nuevaMatricula.EstudianteId);
    if (estudiante is null)
    {
        return Results.NotFound(new { mensaje = $"El estudiante con ID {nuevaMatricula.EstudianteId} no existe en el sistema." });
    }

    // 2. Verificar existencia de la asignatura
    var asignatura = asignaturas.FirstOrDefault(a => a.Id == nuevaMatricula.AsignaturaId);
    if (asignatura is null)
    {
        return Results.NotFound(new { mensaje = $"La asignatura con ID {nuevaMatricula.AsignaturaId} no existe en el sistema." });
    }

    // 3. Validar que la asignatura se encuentre Activa == true
    if (!asignatura.Activa)
    {
        return Results.BadRequest(new
        {
            mensaje = $"No es posible matricular: La asignatura '{asignatura.Nombre}' ({asignatura.Codigo}) se encuentra inactiva."
        });
    }

    // 4. Validación de campos de año y periodo lectivo
    int anio = nuevaMatricula.Anio > 0 ? nuevaMatricula.Anio : 2026;
    string periodo = string.IsNullOrWhiteSpace(nuevaMatricula.Periodo) ? "2026-1" : nuevaMatricula.Periodo.Trim();

    // 5. Control de duplicados por periodo: No puede matricular la misma asignatura 2 veces en el mismo año y periodo lectivo
    bool matriculaDuplicada = matriculas.Any(m =>
        m.EstudianteId == nuevaMatricula.EstudianteId &&
        m.AsignaturaId == nuevaMatricula.AsignaturaId &&
        m.Anio == anio &&
        string.Equals(m.Periodo, periodo, StringComparison.OrdinalIgnoreCase));

    if (matriculaDuplicada)
    {
        return Results.BadRequest(new
        {
            mensaje = $"El estudiante '{estudiante.Nombre}' ya se encuentra matriculado en la asignatura '{asignatura.Nombre}' para el periodo lectivo {periodo} del año {anio}."
        });
    }

    // Asignación de ID autoincremental para la matrícula
    int nuevoId = nuevaMatricula.Id > 0 && !matriculas.Any(m => m.Id == nuevaMatricula.Id)
        ? nuevaMatricula.Id
        : (matriculas.Any() ? matriculas.Max(m => m.Id) + 1 : 1);

    var matriculaRegistrada = nuevaMatricula with { Id = nuevoId, Anio = anio, Periodo = periodo };
    matriculas.Add(matriculaRegistrada);

    return Results.Created($"/api/matriculas/{matriculaRegistrada.Id}", matriculaRegistrada);
})
.WithName("RegistrarMatricula")
.WithTags("Matriculas")
.WithSummary("Matricula un estudiante en una asignatura registrando Año y Periodo con todas las validaciones de negocio.");

// --------------------------------------------------------------------
// Endpoint 5: GET /api/estudiantes/{id}/asignaturas
// Consultar el historial de asignaturas matriculadas por un estudiante específico.
// Preserva el historial académico incluso para asignaturas que hayan sido inactivadas.
// --------------------------------------------------------------------
app.MapGet("/api/estudiantes/{id:int}/asignaturas", (int id) =>
{
    var estudiante = estudiantes.FirstOrDefault(e => e.Id == id);
    if (estudiante is null)
    {
        return Results.NotFound(new { mensaje = $"No se encontró el estudiante con ID {id}." });
    }

    // Se consultan las asignaturas matriculadas por el estudiante uniendo con la lista de asignaturas
    var historial = (from m in matriculas
                     where m.EstudianteId == id
                     join a in asignaturas on m.AsignaturaId equals a.Id
                     select new
                     {
                         matriculaId = m.Id,
                         estudianteId = m.EstudianteId,
                         asignaturaId = a.Id,
                         nombreAsignatura = a.Nombre,
                         codigoAsignatura = a.Codigo,
                         creditos = a.Creditos,
                         anio = m.Anio,
                         periodo = m.Periodo,
                         activa = a.Activa
                     }).ToList();

    return Results.Ok(historial);
})
.WithName("ObtenerHistorialPorEstudiante")
.WithTags("Estudiantes")
.WithSummary("Consulta el historial de asignaturas matriculadas por un estudiante específico.");

// ====================================================================
// 5. ENDPOINTS ADICIONALES DE APOYO (Para facilitar pruebas en Swagger)
// ====================================================================

// Consultar todas las asignaturas (incluidas inactivas, útil para verificar borrado lógico)
app.MapGet("/api/asignaturas/todas", () => Results.Ok(asignaturas))
    .WithTags("Asignaturas")
    .WithSummary("Consulta todas las asignaturas registradas, tanto activas como inactivas (apoyo para auditar borrado lógico).");

// Consultar una asignatura específica por ID
app.MapGet("/api/asignaturas/{id:int}", (int id) =>
{
    var asignatura = asignaturas.FirstOrDefault(a => a.Id == id);
    return asignatura is not null
        ? Results.Ok(asignatura)
        : Results.NotFound(new { mensaje = $"No se encontró la asignatura con ID {id}." });
})
.WithTags("Asignaturas")
.WithSummary("Consulta el detalle de una asignatura específica por su ID.");

// Consultar lista de estudiantes registrados
app.MapGet("/api/estudiantes", () => Results.Ok(estudiantes))
    .WithTags("Estudiantes")
    .WithSummary("Consulta el listado de estudiantes disponibles para realizar pruebas de matrícula.");

// Consultar listado completo de matrículas registradas
app.MapGet("/api/matriculas", () => Results.Ok(matriculas))
    .WithTags("Matriculas")
    .WithSummary("Consulta todas las matrículas registradas en el sistema.");

// Restablecer base de datos en memoria a su estado inicial
app.MapPost("/api/reset", () =>
{
    asignaturas.Clear();
    asignaturas.AddRange(ObtenerAsignaturasIniciales());
    estudiantes.Clear();
    estudiantes.AddRange(ObtenerEstudiantesIniciales());
    matriculas.Clear();
    return Results.Ok(new
    {
        mensaje = "Datos de prueba en memoria restablecidos exitosamente a su estado inicial.",
        asignaturas = asignaturas.Count,
        estudiantes = estudiantes.Count,
        matriculas = matriculas.Count
    });
});

// Ruta de respaldo para la Interfaz Gráfica (SPA)
app.MapFallbackToFile("index.html");

app.Run();

// ====================================================================
// 6. DEFINICIÓN DEL MODELO DE DATOS (RECORDS)
// ====================================================================

// 1. Modelo de Estudiante
record Estudiante(int Id, string Nombre, string Carrera);

// 2. Modelo de Asignatura (Incluye estado para Borrado Lógico)
record Asignatura(int Id, string Nombre, string Codigo, int Creditos, bool Activa = true);

// 3. Modelo de Matrícula (Entidad Intermedia con Año y Periodo Académico)
record Matricula(int Id, int EstudianteId, int AsignaturaId, int Anio = 2026, string Periodo = "2026-1");
