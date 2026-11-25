package main

import (
	"strings"
	"testing"
	"unsafe"

	"golang.org/x/sys/windows"
	"net-merge/internal/engine"
	"net-merge/internal/nics"
)

func TestHealthyLinkCount(t *testing.T) {
	for _, tc := range []struct {
		name  string
		slots []engine.SlotStats
		want  int
	}{
		{"all links lost", []engine.SlotStats{{Present: true, State: "no internet"}, {Present: false, State: "up"}}, 0},
		{"new adapter checking", []engine.SlotStats{{Present: true, State: "up"}, {Present: true, State6: "checking"}}, 1},
		{"dual-stack link counts once", []engine.SlotStats{{Present: true, State: "up", State6: "up"}}, 1},
		{"two links ready", []engine.SlotStats{{Present: true, State: "up"}, {Present: true, State6: "up"}}, 2},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := healthyLinkCount(tc.slots); got != tc.want {
				t.Fatalf("healthyLinkCount = %d, want %d", got, tc.want)
			}
		})
	}
}

func TestWorkerReport(t *testing.T) {
	const session = "1f10741242014458a661e673f90fdeac"
	name := windows.StringToUTF16Ptr("Local\\DonutBoostMerge-" + session + "-Report")
	mapping, err := windows.CreateFileMapping(windows.InvalidHandle, nil, windows.PAGE_READWRITE, 0, 4096, name)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CloseHandle(mapping)
	addr, err := windows.MapViewOfFile(mapping, windows.FILE_MAP_READ, 0, 0, 4096)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.UnmapViewOfFile(addr)
	write, closeReport, err := openReport(session)
	if err != nil {
		t.Fatal(err)
	}
	defer closeReport()
	report := summarizeLinks([]engine.SlotStats{{Name: "Wi-Fi", State: "up", Present: true, BytesIn: 42}},
		[]nics.Skip{{Name: "Ethernet 2", Reason: "no default gateway (no internet route)"}})
	write(report)
	sequence, length := *(*uint32)(unsafe.Pointer(addr)), *(*uint32)(unsafe.Pointer(addr + 4))
	received := string(unsafe.Slice((*byte)(unsafe.Pointer(addr+8)), length))
	if sequence != 2 || received != report || !strings.Contains(received, "data received") || !strings.Contains(received, "Ethernet 2 excluded") {
		t.Fatalf("sequence=%d report=%q", sequence, received)
	}
}
