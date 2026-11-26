package engine

import "testing"

func TestDonutFlowScorePrefersSpareCapacity(t *testing.T) {
	saturated := &Slot{weight: [2]float64{1}, peak: [2]float64{20_000_000}, linkRateIn: 20_000_000}
	spare := &Slot{weight: [2]float64{0.9}, peak: [2]float64{18_000_000}}
	if donutFlowScore(spare, Fam4, 1, true) >= donutFlowScore(saturated, Fam4, 1, true) {
		t.Fatal("a link with spare capacity should outrank the saturated link")
	}
	if donutFlowScore(saturated, Fam4, 1, false) != 2 {
		t.Fatal("manual weights should ignore throughput measurements")
	}
	slow := &Slot{weight: [2]float64{0.25}, peak: [2]float64{5_000_000}}
	if donutFlowScore(saturated, Fam4, 1, true) >= donutFlowScore(slow, Fam4, 0, true) {
		t.Fatal("a busy fast link should still outrank a much slower idle tether")
	}
	unmeasured := &Slot{weight: [2]float64{1}, linkRateIn: 20_000_000}
	if donutFlowScore(unmeasured, Fam4, 1, true) != 2 {
		t.Fatal("an unmeasured link should keep the original score")
	}
}

func TestDonutFlowScoreAppliesNewCapBeforeOldPeakDecays(t *testing.T) {
	capped := &Slot{weight: [2]float64{1}, peak: [2]float64{15_000_000}}
	free := &Slot{weight: [2]float64{0.5}, peak: [2]float64{7_500_000}}
	capped.limit.Store(&limitPair{mbitPerSec: 5})
	if donutFlowScore(capped, Fam4, 0, true) <= donutFlowScore(free, Fam4, 1, true) {
		t.Fatal("a fresh speed cap should reduce scheduled work immediately")
	}
}
