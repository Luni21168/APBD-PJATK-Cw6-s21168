using ClinicAppointmentsApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicAppointmentsApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentListDto>>> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        const string sql = @"
SELECT
    a.IdAppointment,
    a.AppointmentDate,
    a.Status,
    a.Reason,
    p.FirstName + N' ' + p.LastName AS PatientFullName,
    p.Email AS PatientEmail
FROM dbo.Appointments a
JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
WHERE (@Status IS NULL OR a.Status = @Status)
  AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
ORDER BY a.AppointmentDate;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<ActionResult<AppointmentDetailsDto>> GetAppointmentById([FromRoute] int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        const string sql = @"
SELECT
    a.IdAppointment,
    a.AppointmentDate,
    a.Status,
    a.Reason,
    a.InternalNotes,
    a.CreatedAt,

    p.IdPatient,
    p.FirstName AS PatientFirstName,
    p.LastName AS PatientLastName,
    p.Email AS PatientEmail,
    p.PhoneNumber AS PatientPhoneNumber,

    d.IdDoctor,
    d.FirstName AS DoctorFirstName,
    d.LastName AS DoctorLastName,
    d.LicenseNumber AS DoctorLicenseNumber,

    s.Name AS SpecializationName
FROM dbo.Appointments a
JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
WHERE a.IdAppointment = @IdAppointment;";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new ErrorResponseDto
            {
                Message = $"Appointment with id {idAppointment} was not found."
            });
        }

        var dto = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFirstName = reader.GetString(reader.GetOrdinal("PatientFirstName")),
            PatientLastName = reader.GetString(reader.GetOrdinal("PatientLastName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFirstName = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
            DoctorLastName = reader.GetString(reader.GetOrdinal("DoctorLastName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };

        return Ok(dto);
    }
}