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

    [HttpPost]
    public async Task<ActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        if (request.AppointmentDate <= DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Appointment date cannot be in the past."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Reason is required."
            });
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string patientSql = @"
SELECT COUNT(1)
FROM dbo.Patients
WHERE IdPatient = @IdPatient AND IsActive = 1;";

        await using (var patientCommand = new SqlCommand(patientSql, connection))
        {
            patientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            var patientExists = (int)(await patientCommand.ExecuteScalarAsync() ?? 0);

            if (patientExists == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Patient does not exist or is inactive."
                });
            }
        }

        const string doctorSql = @"
SELECT COUNT(1)
FROM dbo.Doctors
WHERE IdDoctor = @IdDoctor AND IsActive = 1;";

        await using (var doctorCommand = new SqlCommand(doctorSql, connection))
        {
            doctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            var doctorExists = (int)(await doctorCommand.ExecuteScalarAsync() ?? 0);

            if (doctorExists == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Doctor does not exist or is inactive."
                });
            }
        }

        const string conflictSql = @"
SELECT COUNT(1)
FROM dbo.Appointments
WHERE IdDoctor = @IdDoctor
  AND AppointmentDate = @AppointmentDate
  AND Status = 'Scheduled';";

        await using (var conflictCommand = new SqlCommand(conflictSql, connection))
        {
            conflictCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            conflictCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);

            var conflictExists = (int)(await conflictCommand.ExecuteScalarAsync() ?? 0);

            if (conflictExists > 0)
            {
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has another scheduled appointment at this time."
                });
            }
        }

        const string insertSql = @"
INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
OUTPUT INSERTED.IdAppointment
VALUES (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled', @Reason);";

        int newId;
        await using (var insertCommand = new SqlCommand(insertSql, connection))
        {
            insertCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            insertCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            insertCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            insertCommand.Parameters.AddWithValue("@Reason", request.Reason);

            newId = (int)(await insertCommand.ExecuteScalarAsync() ?? 0);
        }

        return CreatedAtAction(nameof(GetAppointmentById), new { idAppointment = newId }, new
        {
            IdAppointment = newId,
            Message = "Appointment created successfully."
        });

    }

    [HttpPut("{idAppointment:int}")]
    public async Task<ActionResult> UpdateAppointment(
        [FromRoute] int idAppointment,
        [FromBody] UpdateAppointmentRequestDto request)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        if (request.AppointmentDate <= DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Appointment date cannot be in the past."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Reason is required."
            });
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string existingSql = @"
SELECT AppointmentDate, Status
FROM dbo.Appointments
WHERE IdAppointment = @IdAppointment;";

        DateTime currentAppointmentDate;
        string currentStatus;

        await using (var existingCommand = new SqlCommand(existingSql, connection))
        {
            existingCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await using var reader = await existingCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return NotFound(new ErrorResponseDto
                {
                    Message = $"Appointment with id {idAppointment} was not found."
                });
            }

            currentAppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"));
            currentStatus = reader.GetString(reader.GetOrdinal("Status"));
        }

        const string patientSql = @"
SELECT COUNT(1)
FROM dbo.Patients
WHERE IdPatient = @IdPatient AND IsActive = 1;";

        await using (var patientCommand = new SqlCommand(patientSql, connection))
        {
            patientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            var patientExists = (int)(await patientCommand.ExecuteScalarAsync() ?? 0);

            if (patientExists == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Patient does not exist or is inactive."
                });
            }
        }

        const string doctorSql = @"
SELECT COUNT(1)
FROM dbo.Doctors
WHERE IdDoctor = @IdDoctor AND IsActive = 1;";

        await using (var doctorCommand = new SqlCommand(doctorSql, connection))
        {
            doctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            var doctorExists = (int)(await doctorCommand.ExecuteScalarAsync() ?? 0);

            if (doctorExists == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Doctor does not exist or is inactive."
                });
            }
        }

        if (currentStatus == "Completed" && request.AppointmentDate != currentAppointmentDate)
        {
            return Conflict(new ErrorResponseDto
            {
                Message = "Cannot change appointment date for a completed appointment."
            });
        }

        const string conflictSql = @"
SELECT COUNT(1)
FROM dbo.Appointments
WHERE IdDoctor = @IdDoctor
  AND AppointmentDate = @AppointmentDate
  AND Status = 'Scheduled'
  AND IdAppointment <> @IdAppointment;";

        await using (var conflictCommand = new SqlCommand(conflictSql, connection))
        {
            conflictCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            conflictCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            conflictCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            var conflictExists = (int)(await conflictCommand.ExecuteScalarAsync() ?? 0);

            if (conflictExists > 0)
            {
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has another scheduled appointment at this time."
                });
            }
        }

        const string updateSql = @"
UPDATE dbo.Appointments
SET IdPatient = @IdPatient,
    IdDoctor = @IdDoctor,
    AppointmentDate = @AppointmentDate,
    Status = @Status,
    Reason = @Reason,
    InternalNotes = @InternalNotes
WHERE IdAppointment = @IdAppointment;";

        await using (var updateCommand = new SqlCommand(updateSql, connection))
        {
            updateCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            updateCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            updateCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            updateCommand.Parameters.AddWithValue("@Status", request.Status);
            updateCommand.Parameters.AddWithValue("@Reason", request.Reason);
            updateCommand.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await updateCommand.ExecuteNonQueryAsync();
        }

        return Ok(new
        {
            Message = "Appointment updated successfully."
        });
    }
    [HttpDelete("{idAppointment:int}")]
    public async Task<ActionResult> DeleteAppointment([FromRoute] int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        const string checkSql = @"
SELECT Status
FROM dbo.Appointments
WHERE IdAppointment = @IdAppointment;";

        string status;

        await using (var checkCommand = new SqlCommand(checkSql, connection))
        {
            checkCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            var result = await checkCommand.ExecuteScalarAsync();

            if (result == null)
            {
                return NotFound(new ErrorResponseDto
                {
                    Message = $"Appointment with id {idAppointment} was not found."
                });
            }

            status = result.ToString()!;
        }

        if (status == "Completed")
        {
            return Conflict(new ErrorResponseDto
            {
                Message = "Completed appointment cannot be deleted."
            });
        }

        const string deleteSql = @"
DELETE FROM dbo.Appointments
WHERE IdAppointment = @IdAppointment;";

        await using (var deleteCommand = new SqlCommand(deleteSql, connection))
        {
            deleteCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);
            await deleteCommand.ExecuteNonQueryAsync();
        }

        return NoContent();
    }
}