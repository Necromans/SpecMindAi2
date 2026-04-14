using Microsoft.EntityFrameworkCore;
using SpecMind.Models;

namespace SpecMind.DataBase
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<ReferenceMaterialFile> ReferenceMaterialFiles => Set<ReferenceMaterialFile>();
        public DbSet<AnalysisHistoryItem> AnalysisHistoryItems => Set<AnalysisHistoryItem>();
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        public DbSet<ChatConversationMessage> ChatConversationMessages => Set<ChatConversationMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(x => x.Email)
                .IsUnique();

            modelBuilder.Entity<ReferenceMaterialFile>()
                .Property(x => x.MaterialType)
                .IsRequired();

            modelBuilder.Entity<ReferenceMaterialFile>()
                .Property(x => x.OriginalFileName)
                .IsRequired();

            modelBuilder.Entity<ReferenceMaterialFile>()
                .Property(x => x.StoredFileName)
                .IsRequired();

            modelBuilder.Entity<ReferenceMaterialFile>()
                .Property(x => x.RelativePath)
                .IsRequired();

            modelBuilder.Entity<AnalysisHistoryItem>()
                .Property(x => x.FileName)
                .IsRequired();

            modelBuilder.Entity<AnalysisHistoryItem>()
                .Property(x => x.SourceType)
                .IsRequired();

            modelBuilder.Entity<AnalysisHistoryItem>()
                .Property(x => x.Verdict)
                .IsRequired();

            modelBuilder.Entity<ChatConversation>()
                .Property(x => x.Title)
                .IsRequired();

            modelBuilder.Entity<ChatConversationMessage>()
                .Property(x => x.Role)
                .IsRequired();

            modelBuilder.Entity<ChatConversationMessage>()
                .Property(x => x.Content)
                .IsRequired();

            modelBuilder.Entity<ChatConversationMessage>()
                .HasOne(x => x.ChatConversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ChatConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}