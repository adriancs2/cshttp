using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SQLite;
using Newtonsoft.Json;

namespace CommonWebAppDemo.engine
{
    public class MemberItem
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("email")]
        public string Email { get; set; }
        [JsonProperty("role")]
        public string Role { get; set; }
        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; set; }
        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }
    }

    public static class Db
    {
        static string _connectionString;

        public static void Init()
        {
            string appData = cshttp.WebApp.MapPath("~/App_Data");
            if (string.IsNullOrEmpty(appData))
            {
                appData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data");
            }
            if (!Directory.Exists(appData))
            {
                Directory.CreateDirectory(appData);
            }

            string uploads = Path.Combine(appData, "uploads");
            if (!Directory.Exists(uploads))
            {
                Directory.CreateDirectory(uploads);
            }

            string dbPath = Path.Combine(appData, "membership.db");
            _connectionString = "Data Source=" + dbPath + ";Version=3;";

            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
CREATE TABLE IF NOT EXISTS members (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE,
    role TEXT NOT NULL,
    avatar_url TEXT,
    created_at TEXT NOT NULL
);";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.ExecuteNonQuery();
                }

                // Seed initial data if empty
                string countSql = "SELECT COUNT(*) FROM members;";
                using (var countCmd = new SQLiteCommand(countSql, conn))
                {
                    long count = (long)countCmd.ExecuteScalar();
                    if (count == 0)
                    {
                        Insert(new MemberItem
                        {
                            Name = "Adrian Tan",
                            Email = "adrian@example.com",
                            Role = "Lead Architect",
                            AvatarUrl = "/assets/images/default-avatar.svg",
                            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                        Insert(new MemberItem
                        {
                            Name = "Sophia Lin",
                            Email = "sophia@example.com",
                            Role = "Senior Engineer",
                            AvatarUrl = "/assets/images/default-avatar.svg",
                            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                        Insert(new MemberItem
                        {
                            Name = "Marcus Vance",
                            Email = "marcus@example.com",
                            Role = "DevOps Specialist",
                            AvatarUrl = "/assets/images/default-avatar.svg",
                            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }
            }
        }

        public static List<MemberItem> GetAll()
        {
            var list = new List<MemberItem>();
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT id, name, email, role, avatar_url, created_at FROM members ORDER BY id DESC;";
                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new MemberItem
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Email = reader.GetString(2),
                            Role = reader.GetString(3),
                            AvatarUrl = reader.IsDBNull(4) ? "/assets/images/default-avatar.svg" : reader.GetString(4),
                            CreatedAt = reader.GetString(5)
                        });
                    }
                }
            }
            return list;
        }

        public static MemberItem GetById(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = "SELECT id, name, email, role, avatar_url, created_at FROM members WHERE id = @id LIMIT 1;";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new MemberItem
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Email = reader.GetString(2),
                                Role = reader.GetString(3),
                                AvatarUrl = reader.IsDBNull(4) ? "/assets/images/default-avatar.svg" : reader.GetString(4),
                                CreatedAt = reader.GetString(5)
                            };
                        }
                    }
                }
            }
            return null;
        }

        public static int Insert(MemberItem member)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
INSERT INTO members (name, email, role, avatar_url, created_at)
VALUES (@name, @email, @role, @avatar_url, @created_at);
SELECT last_insert_rowid();";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@name", member.Name ?? "");
                    cmd.Parameters.AddWithValue("@email", member.Email ?? "");
                    cmd.Parameters.AddWithValue("@role", member.Role ?? "Member");
                    cmd.Parameters.AddWithValue("@avatar_url", member.AvatarUrl ?? "/assets/images/default-avatar.svg");
                    cmd.Parameters.AddWithValue("@created_at", string.IsNullOrEmpty(member.CreatedAt) ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : member.CreatedAt);

                    long newId = (long)cmd.ExecuteScalar();
                    member.Id = (int)newId;
                    return member.Id;
                }
            }
        }

        public static bool Update(MemberItem member)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
UPDATE members 
SET name = @name, email = @email, role = @role, avatar_url = @avatar_url
WHERE id = @id;";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", member.Id);
                    cmd.Parameters.AddWithValue("@name", member.Name ?? "");
                    cmd.Parameters.AddWithValue("@email", member.Email ?? "");
                    cmd.Parameters.AddWithValue("@role", member.Role ?? "Member");
                    cmd.Parameters.AddWithValue("@avatar_url", member.AvatarUrl ?? "/assets/images/default-avatar.svg");

                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public static bool Delete(int id)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                string sql = "DELETE FROM members WHERE id = @id;";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
    }
}
