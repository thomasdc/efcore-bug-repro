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
            public Data UpdateData { get; private set; }

            private StatusUpdateLogLine()
            {
                // Required by EF Core
            }

            public StatusUpdateLogLine(Aggregate aggregate, Status? fromStatus, Status? toStatus) : base(aggregate)
            {
                UpdateData = new Data
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
                return $"Status updated from '{UpdateData.From}' to '{UpdateData.To}'";
            }
        }

        public class OwnerChangedLogLine : Program.LogLine
        {
            public Data UpdateData { get; private set; }

            private OwnerChangedLogLine()
            {
                // Required by EF Core
            }

            public OwnerChangedLogLine(Aggregate aggregate, string fromOwner, string toOwner) : base(aggregate)
            {
                UpdateData = new Data
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
                return $"Owner updated from '{UpdateData.From}' to '{UpdateData.To}'";
            }
        }

        public class EntityContext : DbContext
        {
            private readonly bool _makeItCrash;

            public EntityContext(bool makeItCrash)
            {
                _makeItCrash = makeItCrash;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseSqlServer(@"Server=(LocalDB)\MSSQLLocalDB;Database=EfCoreBugRepro;Trusted_Connection=True;MultipleActiveResultSets=True;");

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Aggregate>().HasKey(_ => _.Id);
                modelBuilder.Entity<Aggregate>().Property(_ => _.Status).HasConversion<string>();
                modelBuilder.Entity<Aggregate>().Property(_ => _.Owner);
                modelBuilder.Entity<Aggregate>().Metadata.FindNavigation($"{nameof(Aggregate.Logs)}").SetPropertyAccessMode(PropertyAccessMode.Field);

                modelBuilder.Entity<LogLine>().HasKey(_ => _.Id);
                modelBuilder.Entity<LogLine>().HasDiscriminator<string>("update-type")
                    .HasValue<StatusUpdateLogLine>("StatusUpdate")
                    .HasValue<OwnerChangedLogLine>("OwnerChanged");
                modelBuilder.Entity<LogLine>().HasOne(_ => _.Aggregate).WithMany(_ => _.Logs);

                modelBuilder.Entity<StatusUpdateLogLine>().Property(_ => _.UpdateData).HasColumnName(_makeItCrash ? "UpdateData" : "StatusUpdateData").HasConversion(
                    data => JsonConvert.SerializeObject(data, new StringEnumConverter()),
                    data => JsonConvert.DeserializeObject<StatusUpdateLogLine.Data>(data, new StringEnumConverter()));

                modelBuilder.Entity<OwnerChangedLogLine>().Property(_ => _.UpdateData).HasColumnName(_makeItCrash ? "UpdateData" : "OwnerChangedData").HasConversion(
                    data => JsonConvert.SerializeObject(data, new StringEnumConverter()),
                    data => JsonConvert.DeserializeObject<OwnerChangedLogLine.Data>(data, new StringEnumConverter()));
            }
        }

        static void Main(string[] args)
        {
            try
            {
                var makeItCrash = true;
                using (var context = new EntityContext(makeItCrash))
                {
                    context.Database.EnsureDeleted();
                    context.Database.EnsureCreated();
                    var aggregate = new Aggregate("foobar");
                    aggregate.UpdateStatus(Status.Active);
                    aggregate.UpdateOwner("barfoo");
                    context.Add(aggregate);
                    context.SaveChanges();
                }

                using (var context = new EntityContext(makeItCrash))
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
