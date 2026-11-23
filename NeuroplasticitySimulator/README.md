# Neuroplasticity Simulator

A desktop image-classification simulator built with C#, .NET 8, Avalonia and SQLite. The app provides a small feed-forward neural network that users can train, modify and damage while watching its performance and network state.

## Run

Install the .NET 8 SDK, then run from this directory:

```sh
dotnet run
```

The SQLite database is created at the platform's local application data directory under `NeuroplasticitySimulator/neuro.db`.

## App screens

- **Login and registration:** create an account or sign in. Passwords must be at least 12 characters and include uppercase and lowercase letters, a number and a special character. Common passwords are rejected. Passwords are stored as Argon2id hashes.
- **Dashboard:** see experiment totals, recent experiment cards, progress and quick actions. Live experiments continue running while another screen is open.
- **New Experiment:** choose a name, input dataset, target cycle count, development stage and learning rule. A new experiment starts with one agent.
- **My Experiments:** reopen, resume or delete saved experiments.
- **Simulation workspace:** inspect the current agent's network, accuracy, elapsed time, estimated time remaining, current classification, activity or weight heatmap, and network health. Pause or resume, stop, change simulation speed, or adjust the selected agent's learning rate, forgetting rate, hidden-layer size and plasticity switch. Select a neuron to apply injury.
- **Results:** review an experiment's accuracy and loss charts, summary and recorded events; export its metrics as CSV.
- **Account Settings:** change the account password.

## Agents and learning

Each experiment can contain up to six agents. Add agents while in the workspace to compare development stages and learning rules on the same dataset. Each agent has its own network and controls; select its tab to inspect it. Charts display the available agents' histories.

The network has an input layer, one adjustable-size hidden layer and an output layer. Hidden neurons use Leaky ReLU; the output layer uses Softmax. Available learning rules are:

- **Backpropagation:** supervised cross-entropy updates with L2 weight regularisation.
- **Hebbian:** Oja-style updates in the hidden layer and class-specific updates in the output layer.
- **Hybrid:** combines the backpropagation and Hebbian update requests.

Development stages (Infant, Child, Teen and Adult) set initial weight range, pretraining amount, learning rate and forgetting rate. Stage, learning rule, learning rate, forgetting rate, hidden-layer size and whether plasticity is enabled can be changed for an agent during a run. Disabling plasticity freezes learning and forgetting but the agent continues processing examples.

Forgetting decays unused connections; sufficiently small weights are pruned. Network injury randomly removes 40% of the selected agent's surviving connections. The network view also lets the user select a hidden or output neuron and remove some or all of its incoming connections. Injury and other changes are recorded as events.

Accuracy is evaluated on up to 200 examples from the dataset's test partition. Metrics are recorded every 10 cycles and on logged events. The simulation advances agents together until each reaches the experiment's target cycle count. Stopping early saves the experiment as Completed; it does not provide a restart-in-place action.

## Input datasets

- **Built-in digits:** generated 8×8 grayscale digit-like images made from simple glyphs with random shifts and noise. This is a lightweight synthetic dataset, not MNIST or the scikit-learn digits dataset used by the earlier Python prototype.
- **CIFAR-10:** select a CIFAR-10 binary batch file such as `data_batch_1.bin`. The app converts up to 2,500 records into 32×32 grayscale images.
- **My images:** select a folder containing at least two class subfolders. Supported image formats are PNG, JPG/JPEG, BMP and GIF. Images are resized to 16×16 grayscale; at least 20 readable images are required. The app makes an 80/20 train/test split.

The chosen dataset and path are stored with the experiment. Reopening an experiment that uses a user-provided dataset requires the source file or folder to remain available at that path.

## Saving and project structure

Accounts, experiments, metrics and compressed agent checkpoints are stored in SQLite. Pausing, stopping, and closing the app save live experiments; live runs are marked Paused when the app closes. Reopened runs restore the network weights, surviving connections, stage, learning rule, cycle, history and events.

- `Core/` contains the network, neurons, datasets, learning rules, agents, sessions and checkpoint serialization.
- `Data/` contains SQLite storage and account/password services.
- `UI/` contains the Avalonia screens, controls and theme; the interface is built in C# rather than XAML.

Existing databases are upgraded with missing simulation columns when opened.
