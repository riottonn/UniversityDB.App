using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace UniversityManagement
{

    public class Student
    {
        public int StudentId { get; set; }
        public int GroupId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string StudentCardNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class ActiveStudentViewModel
    {
        public int StudentId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string StudentCardNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string SpecialtyName { get; set; } = string.Empty;
        public string FacultyName { get; set; } = string.Empty;
    }

    public interface IStudentRepository
    {
        Task<int> AddAsync(Student student, string operatorUser);
        Task SoftDeleteAsync(int studentId, string operatorUser);
        Task<IEnumerable<ActiveStudentViewModel>> GetActiveStudentsAsync();
    }

    public interface IGradeRepository
    {
        Task UpsertGradeAsync(int examId, int studentId, decimal score, string operatorUser);
    }

    public interface IUnitOfWork : IAsyncDisposable
    {
        IStudentRepository Students { get; }
        IGradeRepository Grades { get; }
        Task CommitAsync();
        Task RollbackAsync();
    }


    public class StudentRepository : IStudentRepository
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        public StudentRepository(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task<int> AddAsync(Student s, string operatorUser)
        {
            using var cmd = new SqlCommand("sp_CreateStudent", _connection, _transaction);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@GroupId", s.GroupId);
            cmd.Parameters.AddWithValue("@FirstName", s.FirstName);
            cmd.Parameters.AddWithValue("@LastName", s.LastName);
            cmd.Parameters.AddWithValue("@StudentCardNumber", s.StudentCardNumber);
            cmd.Parameters.AddWithValue("@Email", s.Email);
            cmd.Parameters.AddWithValue("@BirthDate", s.BirthDate);
            cmd.Parameters.AddWithValue("@CreatedBy", operatorUser);

            var outputId = new SqlParameter("@NewStudentId", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(outputId);

            await cmd.ExecuteNonQueryAsync();
            return (int)outputId.Value;
        }

        public async Task SoftDeleteAsync(int studentId, string operatorUser)
        {
            using var cmd = new SqlCommand("sp_SoftDeleteStudent", _connection, _transaction);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@StudentId", studentId);
            cmd.Parameters.AddWithValue("@DeletedBy", operatorUser);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ActiveStudentViewModel>> GetActiveStudentsAsync()
        {
            var list = new List<ActiveStudentViewModel>();
            using var cmd = new SqlCommand("SELECT * FROM vw_ActiveStudents", _connection, _transaction);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ActiveStudentViewModel
                {
                    StudentId = reader.GetInt32(0),
                    FirstName = reader.GetString(1),
                    LastName = reader.GetString(2),
                    StudentCardNumber = reader.GetString(3),
                    Email = reader.GetString(4),
                    GroupName = reader.GetString(5),
                    SpecialtyName = reader.GetString(6),
                    FacultyName = reader.GetString(7)
                });
            }
            return list;
        }
    }

    public class GradeRepository : IGradeRepository
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        public GradeRepository(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task UpsertGradeAsync(int examId, int studentId, decimal score, string operatorUser)
        {
            using var cmd = new SqlCommand("sp_UpsertGrade", _connection, _transaction);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@ExamId", examId);
            cmd.Parameters.AddWithValue("@StudentId", studentId);
            cmd.Parameters.AddWithValue("@Score", score);
            cmd.Parameters.AddWithValue("@OperatorUser", operatorUser);
            await cmd.ExecuteNonQueryAsync();
        }
    }


    public class UnitOfWork : IUnitOfWork
    {
        private readonly SqlConnection _connection;
        private SqlTransaction _transaction;
        private bool _disposed;

        public IStudentRepository Students { get; }
        public IGradeRepository Grades { get; }

        public UnitOfWork(string connectionString)
        {
            _connection = new SqlConnection(connectionString);
            _connection.Open();
            _transaction = _connection.BeginTransaction();

            Students = new StudentRepository(_connection, _transaction);
            Grades = new GradeRepository(_connection, _transaction);
        }

        public async Task CommitAsync()
        {
            try
            {
                await _transaction.CommitAsync();
            }
            catch
            {
                await RollbackAsync();
                throw;
            }
        }

        public async Task RollbackAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                if (_transaction != null) await _transaction.DisposeAsync();
                if (_connection != null) await _connection.DisposeAsync();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }

    internal class Program
    {
        private static readonly string ConnectionString = "Server=localhost\\SQLEXPRESS;Database=UniversityDB;Trusted_Connection=True;TrustServerCertificate=True;";

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            while (true)
            {
                Console.Clear();
                Console.WriteLine("==================================================");
                Console.WriteLine("        СИСТЕМА УПРАВЛІННЯ УНІВЕРСИТЕТОМ");
                Console.WriteLine("        (Repository + Unit of Work Demo)");
                Console.WriteLine("==================================================");
                Console.WriteLine("1. Показати список активних студентів (з VIEW)");
                Console.WriteLine("2. Додати нового студента та виставити оцінку (UoW)");
                Console.WriteLine("3. Виставити / оновити оцінку студенту (SP)");
                Console.WriteLine("4. М'яке видалення студента (Soft Delete + Audit)");
                Console.WriteLine("0. Вихід");
                Console.WriteLine("==================================================");
                Console.Write("Оберіть дію: ");

                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await ShowActiveStudentsAsync();
                        break;
                    case "2":
                        await CreateStudentWithGradeAsync();
                        break;
                    case "3":
                        await AddOrUpdateGradeAsync();
                        break;
                    case "4":
                        await SoftDeleteStudentAsync();
                        break;
                    case "0":
                        Console.WriteLine("\nЗавершення роботи. До побачення!");
                        return;
                    default:
                        Console.WriteLine("\nНевірний вибір. Спробуйте ще раз.");
                        break;
                }

                Console.WriteLine("\nНатисніть будь-яку клавішу для повернення до меню...");
                Console.ReadKey();
            }
        }

        private static async Task ShowActiveStudentsAsync()
        {
            Console.WriteLine("\n--- Список активних студентів (vw_ActiveStudents) ---");
            try
            {
                await using var uow = new UnitOfWork(ConnectionString);
                var students = await uow.Students.GetActiveStudentsAsync();

                Console.WriteLine("{0,-4} | {1,-20} | {2,-12} | {3,-10} | {4,-25}", "ID", "ПІБ", "Квиток", "Група", "Спеціальність");
                Console.WriteLine(new string('-', 85));

                foreach (var s in students)
                {
                    Console.WriteLine("{0,-4} | {1,-20} | {2,-12} | {3,-10} | {4,-25}",
                        s.StudentId, $"{s.LastName} {s.FirstName}", s.StudentCardNumber, s.GroupName, s.SpecialtyName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Помилка зчитування]: {ex.Message}");
            }
        }

        private static async Task CreateStudentWithGradeAsync()
        {
            Console.WriteLine("\n--- Реєстрація студента з виставленням оцінки ---");

            Console.Write("Введіть ім'я: ");
            string firstName = Console.ReadLine() ?? "Студент";

            Console.Write("Введіть прізвище: ");
            string lastName = Console.ReadLine() ?? "Тестовий";

            Console.Write("Введіть номер студентського (наприклад, KB990011): ");
            string card = Console.ReadLine() ?? ("KB" + DateTime.Now.Ticks.ToString().Substring(12));

            Console.Write("Введіть email: ");
            string email = Console.ReadLine() ?? $"{card.ToLower()}@univ.kiev.ua";

            Console.Write("Введіть оцінку за перший екзамен (0 - 100): ");
            if (!decimal.TryParse(Console.ReadLine(), out decimal score))
            {
                score = 85.0m;
            }

            await using var uow = new UnitOfWork(ConnectionString);
            try
            {
                var student = new Student
                {
                    GroupId = 1,
                    FirstName = firstName,
                    LastName = lastName,
                    StudentCardNumber = card,
                    Email = email,
                    BirthDate = new DateTime(2005, 1, 1)
                };

                Console.WriteLine("-> Створення студента через збережену процедуру...");
                int newId = await uow.Students.AddAsync(student, operatorUser: "admin_deanery");

                Console.WriteLine($"-> Фіксація оцінки {score} для студента ID={newId}...");
                await uow.Grades.UpsertGradeAsync(examId: 1, studentId: newId, score: score, operatorUser: "prof_sydorenko");

                await uow.CommitAsync();
                Console.WriteLine($"\n[УСПІХ]: Студента '{lastName} {firstName}' успішно створено з ID={newId}, транзакцію підтверджено!");
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync();
                Console.WriteLine($"\n[ПОМИЛКА ТРАНЗАКЦІЇ]: {ex.Message}");
                Console.WriteLine("-> Відкат усіх змін виконано успішно.");
            }
        }

        private static async Task AddOrUpdateGradeAsync()
        {
            Console.WriteLine("\n--- Внесення або оновлення оцінки ---");
            Console.Write("Введіть ID студента: ");
            if (!int.TryParse(Console.ReadLine(), out int studentId)) return;

            Console.Write("Введіть нову оцінку (0 - 100): ");
            if (!decimal.TryParse(Console.ReadLine(), out decimal score)) return;

            await using var uow = new UnitOfWork(ConnectionString);
            try
            {
                await uow.Grades.UpsertGradeAsync(examId: 1, studentId: studentId, score: score, operatorUser: "prof_sydorenko");
                await uow.CommitAsync();
                Console.WriteLine("[УСПІХ]: Оцінку успішно зафіксовано!");
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync();
                Console.WriteLine($"[ПОМИЛКА]: {ex.Message}");
            }
        }

        private static async Task SoftDeleteStudentAsync()
        {
            Console.WriteLine("\n--- М'яке видалення студента (Soft Delete) ---");
            Console.Write("Введіть ID студента для видалення: ");
            if (!int.TryParse(Console.ReadLine(), out int studentId)) return;

            await using var uow = new UnitOfWork(ConnectionString);
            try
            {
                await uow.Students.SoftDeleteAsync(studentId, operatorUser: "dekanat_inspector");
                await uow.CommitAsync();
                Console.WriteLine($"[УСПІХ]: Студента ID={studentId} позначено як видаленого. Тригер зафіксував запис в AuditLogs!");
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync();
                Console.WriteLine($"[ПОМИЛКА]: {ex.Message}");
            }
        }
    }
}