# Neuroplasticity Simulator (C# / Avalonia / SQLite)

Implements the design in *NeuroplasticitySimulator.docx*. Requires the .NET 8 SDK:

    dotnet run

## Screens (follow the UI designs in section 1.6.1)
- **Login / Sign Up** (1.6.1.1, 1.6.1.2): black screen, red title and brain emblem, boxed fields, blue button, Register / Log in links, eye toggles on the sign-up passwords.
- **Dashboard** (1.6.1.3): welcome banner, totals, experiment cards with **Start/Resume, Pause, Stop**, quick actions, progress chart.
  Simulations keep running in the background while you are on the dashboard.
- **Simulation workspace** (1.6.1.4): iteration / accuracy / elapsed / ETA, Pause and Stop, neural network view with a Neuron Actions (injury) panel,
  accuracy chart, neural-activity heatmap, plasticity insights, current task, network health, simulation controls.
- **Agents:** a new simulation starts with **one** agent. Use **＋ Add agent** (up to 6) to compare more on the same images; click a tab to
  inspect/control that agent, ✕ removes one. Charts overlay all agents.
- Also: New Experiment, My Experiments, Results (charts, summary, CSV export), Account Settings (change password).

## Simulation states
Running -> Pause (saved, resumable) -> Resume, or **Stop** = finish and save as Completed. Reaching the target cycle count also completes it.

## Layout
`Core/` neurons, network, learning rules, stages, forgetting/pruning, injury, datasets, sessions, snapshots.
`Data/` SQLite (Users, Simulations, SimulationMetrics, Checkpoints), password policy, Argon2id.
`UI/` Theme, controls, Login, MainWindow (shell), Views (dashboard etc.), WorkspaceView. UI is built in code (no XAML).

## Notes
- An older `neuro.db` is upgraded automatically (new columns are added).
- Not built: Google sign-in, experiment comparison screen, remember-me / forgot-password.
