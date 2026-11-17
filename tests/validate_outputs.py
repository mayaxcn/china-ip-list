#!/usr/bin/env python3
"""Validate generated routes and the nested-APNIC regression case."""

import ipaddress
import pathlib
import sys


def read_cidrs(path):
    networks = []
    for raw in path.read_text().splitlines():
        if not raw.strip():
            continue
        network = ipaddress.ip_network(raw.strip(), strict=True)
        networks.append(network)
    if networks != sorted(networks, key=lambda n: (n.version, int(n.network_address), n.prefixlen)):
        raise AssertionError(f"{path} is not sorted")
    for left, right in zip(networks, networks[1:]):
        if left.overlaps(right):
            raise AssertionError(f"overlapping networks in {path}: {left} and {right}")
    return networks


def validate_family(root, suffix, expected_ip):
    cidr_path = root / f"chnroute{suffix}.txt"
    range_path = root / f"chn_ip{suffix}.txt"
    networks = read_cidrs(cidr_path)
    ranges = range_path.read_text().splitlines()
    if len(networks) != len(ranges):
        raise AssertionError(f"{cidr_path} and {range_path} have different lengths")

    for network, raw_range in zip(networks, ranges):
        start, end = raw_range.split()
        if ipaddress.ip_address(start) != network.network_address:
            raise AssertionError(f"wrong start for {network}: {raw_range}")
        if ipaddress.ip_address(end) != network.broadcast_address:
            raise AssertionError(f"wrong end for {network}: {raw_range}")

    target = ipaddress.ip_address(expected_ip)
    if not any(target in network for network in networks):
        raise AssertionError(f"nested CN regression address is missing: {expected_ip}")
    return len(networks)


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: validate_outputs.py OUTPUT_DIRECTORY")
    root = pathlib.Path(sys.argv[1])
    v4 = validate_family(root, "", "8.141.31.165")
    v6 = validate_family(root, "_v6", "240e:1234::1")
    print(f"validated {v4} IPv4 and {v6} IPv6 CIDRs")


if __name__ == "__main__":
    main()
