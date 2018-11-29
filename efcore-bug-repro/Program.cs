using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace efcore_bug_repro
{
    class Program
    {
        public abstract class Entity
        {
            public long Id { get; private set; }
        }

        public class Aggregate : Entity
        {
            public string Owner { get; private set; }

            public Status? Status { get; private set; }

            private readonly List<LogLine> _logs = new List<LogLine>();
            public IReadOnlyList<LogLine> Logs => _logs.AsReadOnly();

            private Aggregate()
            {
                // Required by EF Core
            }

            public Aggregate(string owner)
            {
                Owner = owner;
            }

            public void UpdateStatus(Status? newStatus)
            {
                var logLine = new StatusUpdateLogLine(this, Status, newStatus);
                Status = newStatus;
                _logs.Add(logLine);
            }

            public void UpdateOwner(string newOwner)
            {
                var logLine = new OwnerChangedLogLine(this, Owner, newOwner);
                Owner = newOwner;
                _logs.Add(logLine);
            }
        }

        public enum Status
        {
            Active,
            Inactive
        }

        public abstract class LogLine : Entity
        {
            public Aggregate Aggregate { get; private set; }

            public string UpdateData { get; protected set; }

            protected LogLine()
            {
                // Required by EF Core
            }

            protected LogLine(Aggregate aggregate)
            {
                Aggregate = aggregate;
            }

            public abstract override string ToString();
        }

        public class StatusUpdateLogLine : LogLine
        {
            public Data StatusUpdateData
            {
                get => JsonConvert.DeserializeObject<Data>(UpdateData, new StringEnumConverter());
                set => UpdateData = JsonConvert.SerializeObject(value, new StringEnumConverter());
            }

            private StatusUpdateLogLine()
            {
                // Required by EF Core
            }

            public StatusUpdateLogLine(Aggregate aggregate, Status? fromStatus, Status? toStatus) : base(aggregate)
            {
                StatusUpdateData = new Data
                {
                    From = fromStatus,
                    To = toStatus
                };
            }

            public class Data
            {
                public Status? From { get; set; }
                public Status? To { get; set; }
            }

            public override string ToString()
            {
                return $"Status updated from '{StatusUpdateData.From}' to '{StatusUpdateData.To}'";
            }
        }

        public class OwnerChangedLogLine : Program.LogLine
        {
            public Data OwnerChangedUpdateData
            {
                get => JsonConvert.DeserializeObject<Data>(UpdateData, new StringEnumConverter());
                set => UpdateData = JsonConvert.SerializeObject(value, new StringEnumConverter());
            }

            private OwnerChangedLogLine()
            {
                // Required by EF Core
            }

            public OwnerChangedLogLine(Aggregate aggregate, string fromOwner, string toOwner) : base(aggregate)
            {
                OwnerChangedUpdateData = new Data
                {
                    From = fromOwner,
                    To = toOwner
                };
            }

            public class Data
            {
                public string From { get; set; }
                public string To { get; set; }
            }

            public override string ToString()
            {
                return $"Owner updated from '{OwnerChangedUpdateData.From}' to '{OwnerChangedUpdateData.To}'";
            }
        }

        public class EntityContext : DbContext
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(@"Server=(LocalDB)\MSSQLLocalDB;Database=EfCoreBugRepro;Trusted_Connection=True;MultipleActiveResultSets=True;");

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Aggregate>().HasKey(_ => _.Id);
                modelBuilder.Entity<Aggregate>().Property(_ => _.Status).HasConversion<string>();
                modelBuilder.Entity<Aggregate>().Property(_ => _.Owner);
                modelBuilder.Entity<Aggregate>().Metadata.FindNavigation($"{nameof(Aggregate.Logs)}").SetPropertyAccessMode(PropertyAccessMode.Field);

                modelBuilder.Entity<LogLine>().HasKey(_ => _.Id);
                modelBuilder.Entity<LogLine>().Property(_ => _.UpdateData);
                modelBuilder.Entity<LogLine>().HasDiscriminator<string>("update-type")
                    .HasValue<StatusUpdateLogLine>("StatusUpdate")
                    .HasValue<OwnerChangedLogLine>("OwnerChanged");
                modelBuilder.Entity<LogLine>().HasOne(_ => _.Aggregate).WithMany(_ => _.Logs);

                modelBuilder.Entity<StatusUpdateLogLine>().Ignore(_ => _.StatusUpdateData);

                modelBuilder.Entity<OwnerChangedLogLine>().Ignore(_ => _.OwnerChangedUpdateData);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                using (var context = new EntityContext())
                {
                    context.Database.EnsureDeleted();
                    context.Database.EnsureCreated();
                    var aggregate = new Aggregate("foobar");
                    aggregate.UpdateStatus(Status.Active);
                    aggregate.UpdateOwner("barfoo");
                    context.Add(aggregate);
                    context.SaveChanges();
                }

                using (var context = new EntityContext())
                {
                    var aggregates = context.Set<Aggregate>().Include(_ => _.Logs).ToList();
                }

                Console.WriteLine("Finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            Console.ReadKey();
        }
    }
}
