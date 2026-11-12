using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace ShackRemote.Core.Service;

/// <summary>
/// Windows-Dienst "ShackRemote" (läuft als SYSTEM, startet mit Windows).
/// Er selbst zeigt nichts an, sondern hält in der Konsolensitzung einen Server-Agenten am Leben
/// ("ShackRemote.exe /agent", als SYSTEM gestartet). Der Agent darf auch auf den Anmelde-/Sperrbildschirm.
/// Außerdem löst der Dienst auf Wunsch des Agenten Strg+Alt+Entf aus (SendSAS ist Diensten vorbehalten).
/// </summary>
public sealed class ShackService : ServiceBase
{
    /// <summary>Dienstname in der Windows-Diensteverwaltung.</summary>
    public const string Name = "ShackRemote";
    /// <summary>Ereignis, über das der Agent Strg+Alt+Entf anfordert.</summary>
    public const string SasEventName = @"Global\ShackRemote_SAS";

    private readonly AutoResetEvent _wake = new(false);
    private EventWaitHandle? _sas;
    private Thread? _watch, _sasThread;
    private volatile bool _stop;
    private Process? _agent;
    private uint _agentSession = ProcessLauncher.NoSession;

    public ShackService()
    {
        ServiceName = Name;
        CanHandleSessionChangeEvent = true;
        CanShutdown = true;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        FileLog.Write("dienst", $"Dienst startet, Version {Protocol.Version}");
        _sas = new EventWaitHandle(false, EventResetMode.AutoReset, SasEventName);
        _watch = new Thread(WatchLoop) { IsBackground = true, Name = "Agent-Wächter" };
        _watch.Start();
        _sasThread = new Thread(SasLoop) { IsBackground = true, Name = "SAS" };
        _sasThread.Start();
    }

    protected override void OnStop() => Shutdown("Dienst wird beendet");

    protected override void OnShutdown() => Shutdown("Windows fährt herunter");

    protected override void OnSessionChange(SessionChangeDescription change)
    {
        FileLog.Write("dienst", $"Sitzungsereignis {change.Reason} (Sitzung {change.SessionId})");
        _wake.Set();
    }

    private void Shutdown(string why)
    {
        FileLog.Write("dienst", why);
        _stop = true;
        _wake.Set();
        _sas?.Set();
        StopAgent();
    }

    /// <summary>Hält den Agenten in der aktiven Konsolensitzung am Leben (Prüfung alle 2 s und bei Sitzungswechsel).</summary>
    private void WatchLoop()
    {
        while (!_stop)
        {
            try
            {
                uint session = ProcessLauncher.ActiveConsoleSession();
                if (session != ProcessLauncher.NoSession)
                {
                    bool running = _agent is { HasExited: false };
                    if (!running || _agentSession != session)
                    {
                        if (running) FileLog.Write("dienst", $"Konsolensitzung gewechselt {_agentSession} -> {session}");
                        else if (_agent is not null) FileLog.Write("dienst", $"Agent beendet (Code {SafeExitCode(_agent)}) – starte neu");
                        StopAgent();
                        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt");
                        _agent = ProcessLauncher.StartAsSystemInSession(exe, "/agent", session);
                        _agentSession = session;
                        FileLog.Write("dienst", $"Agent gestartet in Sitzung {session} (PID {_agent.Id})");
                    }
                }
            }
            catch (Exception ex)
            {
                FileLog.Write("dienst", $"Agent-Start fehlgeschlagen: {ex.Message}");
            }
            _wake.WaitOne(2000);
        }
    }

    private void SasLoop()
    {
        while (!_stop)
        {
            if (_sas!.WaitOne(1000) && !_stop)
            {
                try
                {
                    SendSAS(false);
                    FileLog.Write("dienst", "Strg+Alt+Entf ausgelöst");
                }
                catch (Exception ex)
                {
                    FileLog.Write("dienst", $"Strg+Alt+Entf fehlgeschlagen: {ex.Message}");
                }
            }
        }
    }

    private void StopAgent()
    {
        var agent = _agent;
        _agent = null;
        if (agent is null) return;
        try
        {
            if (!agent.HasExited)
            {
                agent.Kill(entireProcessTree: true);
                agent.WaitForExit(3000);
            }
        }
        catch { /* schon weg */ }
        agent.Dispose();
    }

    private static string SafeExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); } catch { return "?"; }
    }

    /// <summary>Simuliert Strg+Alt+Entf (sas.dll). Erfordert die Richtlinie SoftwareSASGeneration (setzt die Einrichtung).</summary>
    [DllImport("sas.dll")]
    private static extern void SendSAS(bool asUser);
}
