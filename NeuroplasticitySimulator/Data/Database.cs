using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Neuroplasticity.Core;

namespace Neuroplasticity.Data;

public record UserRecord(long Id, string Username);
public record ExperimentInfo(long Id, string Name, string Status, string Modified, string Architecture, int Progress,
                             int RuntimeSec, int TargetCycles, string DataKind, string DataPath);

/// <summary>SQLite storage (spec 2.6.1): Users, Simulations, SimulationMetrics, Checkpoints with foreign keys.</summary>
public class Database
{
    readonly string cs;

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        cs = $"Data Source={path}";
        using var c = Open();
        Exec(c, null, @"
CREATE TABLE IF NOT EXISTS Users(
  UserId INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
  Email TEXT, PasswordHash TEXT NOT NULL, CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Simulations(
  SimulationId INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId INTEGER NOT NULL REFERENCES Users(UserId) ON DELETE CASCADE,
  Name TEXT NOT NULL, Status TEXT NOT NULL, Created TEXT NOT NULL, LastModified TEXT NOT NULL,
  Architecture TEXT NOT NULL, Progress INTEGER NOT NULL,
  RuntimeSec INTEGER NOT NULL DEFAULT 0, TargetCycles INTEGER NOT NULL DEFAULT 5000,
  DataKind TEXT NOT NULL DEFAULT 'digits', DataPath TEXT);
CREATE TABLE IF NOT EXISTS SimulationMetrics(
  MetricId INTEGER PRIMARY KEY AUTOINCREMENT,
  SimulationId INTEGER NOT NULL REFERENCES Simulations(SimulationId) ON DELETE CASCADE,
  Agent TEXT NOT NULL, Cycle INTEGER NOT NULL, Accuracy REAL, Loss REAL, SynapsesAlive INTEGER, Event TEXT);
CREATE TABLE IF NOT EXISTS Checkpoints(
  CheckpointId INTEGER PRIMARY KEY AUTOINCREMENT,
  SimulationId INTEGER NOT NULL REFERENCES Simulations(SimulationId) ON DELETE CASCADE,
  Agent TEXT NOT NULL, Created TEXT NOT NULL, Snapshot BLOB NOT NULL);");
        // upgrade databases created by the earlier version
        foreach (var col in new[] { "RuntimeSec INTEGER NOT NULL DEFAULT 0", "TargetCycles INTEGER NOT NULL DEFAULT 5000", "DataKind TEXT NOT NULL DEFAULT 'digits'", "DataPath TEXT" })
            try { Exec(c, null, "ALTER TABLE Simulations ADD COLUMN " + col); } catch (SqliteException) { }
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(cs);
        c.Open();
        Exec(c, null, "PRAGMA foreign_keys=ON;");
        return c;
    }

    static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);   // always parameterised
        cmd.ExecuteNonQuery();
    }

    // ---------- accounts ----------
    public bool CreateUser(string username, string email, string hash)
    {
        try
        {
            using var c = Open();
            Exec(c, null, "INSERT INTO Users(Username,Email,PasswordHash,CreatedAt) VALUES($u,$e,$h,$d)",
                ("$u", username), ("$e", string.IsNullOrWhiteSpace(email) ? null : email), ("$h", hash), ("$d", DateTime.UtcNow.ToString("o")));
            return true;
        }
        catch (SqliteException) { return false; }   // username already taken
    }

    public (UserRecord user, string hash)? FindUser(string username)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT UserId,Username,PasswordHash FROM Users WHERE Username=$u";
        cmd.Parameters.AddWithValue("$u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (new UserRecord(r.GetInt64(0), r.GetString(1)), r.GetString(2)) : null;
    }

    // ---------- experiments ----------
    /// <summary>Insert or update an experiment together with its metrics and resumable checkpoints.</summary>
    public void SaveSession(long userId, SimulationSession s)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var p = new (string, object)[] { ("$n", s.Name), ("$s", s.Status), ("$m", now), ("$a", s.Architecture), ("$p", s.Progress),
            ("$r", (int)s.Elapsed.TotalSeconds), ("$t", s.TargetCycles), ("$k", s.DataKind), ("$d", s.DataPath) };
        if (s.DbId == null)
        {
            Exec(c, tx, @"INSERT INTO Simulations(UserId,Name,Status,Created,LastModified,Architecture,Progress,RuntimeSec,TargetCycles,DataKind,DataPath)
                          VALUES($u,$n,$s,$m,$m,$a,$p,$r,$t,$k,$d)", p.Append(("$u", (object)userId)).ToArray());
            using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = "SELECT last_insert_rowid()";
            s.DbId = Convert.ToInt64(q.ExecuteScalar());
        }
        else
        {
            Exec(c, tx, @"UPDATE Simulations SET Name=$n,Status=$s,LastModified=$m,Architecture=$a,Progress=$p,RuntimeSec=$r,TargetCycles=$t,DataKind=$k,DataPath=$d
                          WHERE SimulationId=$i AND UserId=$u", p.Append(("$i", (object)s.DbId.Value)).Append(("$u", (object)userId)).ToArray());
            Exec(c, tx, "DELETE FROM SimulationMetrics WHERE SimulationId=$i", ("$i", s.DbId.Value));
            Exec(c, tx, "DELETE FROM Checkpoints WHERE SimulationId=$i", ("$i", s.DbId.Value));
        }
        long id = s.DbId.Value;
        foreach (var a in s.Agents)
        {
            foreach (var m in a.History)
                Exec(c, tx, "INSERT INTO SimulationMetrics(SimulationId,Agent,Cycle,Accuracy,Loss,SynapsesAlive) VALUES($s,$a,$c,$acc,$l,$n)",
                    ("$s", id), ("$a", a.Id), ("$c", m.Cycle), ("$acc", m.Accuracy), ("$l", m.Loss), ("$n", m.Alive));
            foreach (var e in a.Events)
                Exec(c, tx, "INSERT INTO SimulationMetrics(SimulationId,Agent,Cycle,Event) VALUES($s,$a,$c,$e)",
                    ("$s", id), ("$a", a.Id), ("$c", e.Cycle), ("$e", e.Label));
            Exec(c, tx, "INSERT INTO Checkpoints(SimulationId,Agent,Created,Snapshot) VALUES($s,$a,$d,$b)",
                ("$s", id), ("$a", a.Id), ("$d", now), ("$b", SimulationSerialiser.Serialise(a.ToSnapshot())));
        }
        tx.Commit();
    }

    public List<ExperimentInfo> ListExperiments(long userId)
    {
        var list = new List<ExperimentInfo>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT SimulationId,Name,Status,LastModified,Architecture,Progress,RuntimeSec,TargetCycles,DataKind,DataPath
                            FROM Simulations WHERE UserId=$u ORDER BY LastModified DESC, SimulationId DESC";
        cmd.Parameters.AddWithValue("$u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ExperimentInfo(r.GetInt64(0), r.GetString(1), r.GetString(2) == "Running" ? "Paused" : r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)));
        return list;
    }

    public void UpdatePassword(long userId, string hash)
    {
        using var c = Open();
        Exec(c, null, "UPDATE Users SET PasswordHash=$h WHERE UserId=$u", ("$h", hash), ("$u", userId));
    }

    public List<byte[]> LoadCheckpoints(long simId, long userId)
    {
        var list = new List<byte[]>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT k.Snapshot FROM Checkpoints k JOIN Simulations s ON s.SimulationId=k.SimulationId
                            WHERE k.SimulationId=$s AND s.UserId=$u ORDER BY k.Agent";
        cmd.Parameters.AddWithValue("$s", simId); cmd.Parameters.AddWithValue("$u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((byte[])r[0]);
        return list;
    }

    public void DeleteExperiment(long simId, long userId)
    {
        using var c = Open();
        Exec(c, null, "DELETE FROM Simulations WHERE SimulationId=$s AND UserId=$u", ("$s", simId), ("$u", userId));
    }

    public string ExportCsv(long simId, long userId)
    {
        var sb = new StringBuilder("agent,cycle,accuracy,loss,synapses_alive,event\n");
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT m.Agent,m.Cycle,m.Accuracy,m.Loss,m.SynapsesAlive,m.Event FROM SimulationMetrics m
                            JOIN Simulations s ON s.SimulationId=m.SimulationId WHERE m.SimulationId=$s AND s.UserId=$u
                            ORDER BY m.Agent,m.Cycle,m.MetricId";
        cmd.Parameters.AddWithValue("$s", simId); cmd.Parameters.AddWithValue("$u", userId);
        using var r = cmd.ExecuteReader();
        var inv = CultureInfo.InvariantCulture;
        while (r.Read())
            sb.AppendLine(string.Join(",", r.GetString(0), r.GetInt32(1),
                r.IsDBNull(2) ? "" : r.GetDouble(2).ToString("0.0", inv), r.IsDBNull(3) ? "" : r.GetDouble(3).ToString("0.000", inv),
                r.IsDBNull(4) ? "" : r.GetInt32(4).ToString(), r.IsDBNull(5) ? "" : "\"" + r.GetString(5).Replace("\"", "\"\"") + "\""));
        return sb.ToString();
    }
}
