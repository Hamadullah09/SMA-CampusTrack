using CampusTrack.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RfidReader> RfidReaders => Set<RfidReader>();
    public DbSet<RawRfidRead> RawRfidReads => Set<RawRfidRead>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<ScheduleEntry> ScheduleEntries => Set<ScheduleEntry>();
    public DbSet<ActivityReport> ActivityReports => Set<ActivityReport>();
    public DbSet<ParentFeedback> ParentFeedback => Set<ParentFeedback>();
    public DbSet<StudentUpload> StudentUploads => Set<StudentUpload>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<Student>().HasIndex(s => s.RegNo).IsUnique();
        mb.Entity<Student>().HasIndex(s => s.RfidEpc).IsUnique().HasFilter("[RfidEpc] IS NOT NULL");
        mb.Entity<Student>()
          .HasOne(s => s.Parent).WithMany(p => p.Students)
          .HasForeignKey(s => s.ParentId).OnDelete(DeleteBehavior.SetNull);
        mb.Entity<Section>().HasIndex(s => new { s.ClassId, s.Name }).IsUnique();
        mb.Entity<RfidReader>().HasIndex(r => r.ReaderCode).IsUnique();
        mb.Entity<RawRfidRead>().HasIndex(r => new { r.ReaderId, r.Epc, r.ReadTime });
        mb.Entity<AttendanceEvent>().HasIndex(a => new { a.StudentId, a.EventTime });
        mb.Entity<AttendanceEvent>().Property(a => a.Direction).HasConversion<string>().HasMaxLength(8);
        mb.Entity<Room>().Property(r => r.RoomType).HasConversion<string>().HasMaxLength(24);
        mb.Entity<Assignment>().HasIndex(a => a.QrToken).IsUnique();
        mb.Entity<Notification>().HasIndex(n => new { n.UserId, n.CreatedAt });

        // avoid multiple-cascade-path errors on SQL Server
        foreach (var fk in mb.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys())
                     .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
