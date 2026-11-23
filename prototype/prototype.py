
import json
import numpy as np
import matplotlib.pyplot as plt
from sklearn.datasets import load_digits

rng = np.random.default_rng(1)
images, labels = load_digits(return_X_y=True)
images = images / 16                     
order = rng.permutation(len(images))
train, test = order[:1200], order[1200:]

STAGES = {"Infant": (0, 0.10, 0.0005), "Child": (50, 0.08, 0.001),
          "Teen": (200, 0.05, 0.002), "Adult": (600, 0.02, 0.003)}

class Brain:
    def __init__(self, stage):
        self.pretrain, self.learn, self.forget = STAGES[stage]
        self.w = rng.normal(0, 0.01, (64, 10))   
        self.alive = np.ones((64, 10), bool)      
        for _ in range(self.pretrain):
            self.step(rng.choice(train))

    def predict(self, x):
        return np.argmax(x @ (self.w * self.alive), axis=-1)

    def step(self, i):
        x, label = images[i], labels[i]
        guess = self.predict(x)
        if guess == label:
            self.w[:, guess] += self.learn * x          
        else:
            self.w[:, guess] -= self.learn * x          
            self.w[:, label] += self.learn * x          
        self.w[x == 0, :] *= (1 - self.forget)          
        self.alive &= np.abs(self.w) > 0.0005           

    def accuracy(self):
        return np.mean(self.predict(images[test]) == labels[test])

    def injure(self, fraction):
        self.alive &= rng.random(self.alive.shape) > fraction

def run(brain, cycles, pool=None, log_every=20):
    pool = train if pool is None else pool
    out = []
    for c in range(1, cycles + 1):
        brain.step(rng.choice(pool))
        if c % log_every == 0:
            out.append(round(brain.accuracy(), 3))
    return out

results = {}
#Test learning curve per state
plt.figure(figsize=(7, 4))
for stage in STAGES:
    b = Brain(stage); acc = run(b, 1000)
    plt.plot(range(20, 1001, 20), acc, label=stage); results[stage] = (acc[0], acc[-1])
plt.xlabel("Cycles (images seen)"); plt.ylabel("Accuracy on unseen images"); plt.title("Learning at each Development Stage")
plt.legend(); plt.ylim(0, 1); plt.grid(alpha=.3); plt.tight_layout(); plt.savefig("figs/1_stages.png", dpi=130); plt.close()

#Test brain injury and plasticity
plt.figure(figsize=(7, 4)); inj = {}
for stage in ("Child", "Adult"):
    b = Brain(stage); before = run(b, 600); b.injure(0.4)
    after = [round(b.accuracy(), 3)] + run(b, 600)
    plt.plot(range(20, 601, 20), before, label=stage)
    plt.plot(range(600, 1201, 20), after, color=plt.gca().lines[-1].get_color())
    inj[stage] = (before[-1], after[0], after[-1])
plt.axvline(600, color="red", linestyle="--"); plt.text(610, 0.05, "injury (40% of synapses removed)", color="red", fontsize=8)
plt.xlabel("Cycles"); plt.ylabel("Accuracy"); plt.title("Injury and recovery"); plt.legend(); plt.ylim(0, 1); plt.grid(alpha=.3)
plt.tight_layout(); plt.savefig("figs/2_injury.png", dpi=130); plt.close()

#Test weights
b = Brain("Child"); shots = [("Start", b.w[:, 3] * b.alive[:, 3])]
run(b, 600); shots.append(("After learning", b.w[:, 3] * b.alive[:, 3]))
b.injure(0.4); shots.append(("After injury", b.w[:, 3] * b.alive[:, 3]))
run(b, 600); shots.append(("After recovery", b.w[:, 3] * b.alive[:, 3]))
fig, axes = plt.subplots(1, 4, figsize=(9, 2.6))
for ax, (title, w) in zip(axes, shots):
    ax.imshow(w.reshape(8, 8), cmap="coolwarm", vmin=-4, vmax=4); ax.set_title(title, fontsize=9); ax.axis("off")
plt.suptitle("Synapse weights feeding the output for digit 3 (red = strong, blue = negative)", fontsize=9)
plt.tight_layout(); plt.savefig("figs/3_weights.png", dpi=130); plt.close()

#Test forgetting
plt.figure(figsize=(7, 4)); forgot = {}
for rate in (0.0005, 0.005):
    b = Brain("Teen"); run(b, 600); b.forget = rate
    alive = [int(b.alive.sum())]; seen_only = [i for i in train if labels[i] < 5]
    for _ in range(30):
        run(b, 100, pool=seen_only, log_every=1000); alive.append(int(b.alive.sum()))
    plt.plot(range(0, 3001, 100), alive, label=f"forgetting rate {rate}"); forgot[rate] = (alive[0], alive[-1])
plt.xlabel("Cycles"); plt.ylabel("Synapses still alive (out of 640)"); plt.title("Unused connections fade away")
plt.legend(); plt.grid(alpha=.3); plt.tight_layout(); plt.savefig("figs/4_forgetting.png", dpi=130); plt.close()

json.dump({"stages": results, "injury": inj, "forgetting": {str(k): v for k, v in forgot.items()}}, open("results.json", "w"))
print(results); print(inj); print(forgot)
