# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

#!/usr/bin/env python3
"""Generate a minimal synthetic PS5 ELF for SharpEmu bring-up testing.

Produces the smallest image SelfLoader accepts: ELF64/LE, ABIVERSION=2
(Gen5/PS5 path), one PT_LOAD R+X segment whose code immediately returns.
Returning pops the dispatcher's return-to-host sentinel, so the run ends
cleanly with reason=ReturnedToHost / ORBIS_GEN2_OK.

Usage: gen_min_elf.py <output.elf>
"""

import struct
import sys

EHDR_SIZE = 64
PHDR_SIZE = 56  # SelfLoader.ValidateElfHeader requires e_phentsize == 56

# xor eax, eax ; ret
CODE = bytes([0x31, 0xC0, 0xC3])


def build() -> bytes:
    code_off = EHDR_SIZE + PHDR_SIZE  # 0x78, code sits right after headers
    filesz = code_off + len(CODE)

    e_ident = bytes([
        0x7F, ord("E"), ord("L"), ord("F"),
        2,  # ELFCLASS64
        1,  # little-endian
        1,  # EV_CURRENT
        9,  # OSABI: FreeBSD (not validated, but matches the real thing)
        2,  # ABIVERSION=2 -> loader selects the PS5 (Gen5) image base
    ]) + bytes(7)

    ehdr = struct.pack(
        "<16sHHIQQQIHHHHHH",
        e_ident,
        2,          # e_type ET_EXEC (not validated)
        62,         # e_machine EM_X86_64 (not validated)
        1,          # e_version
        code_off,   # e_entry — loader rebases: entry = e_entry + 0x800000000
        EHDR_SIZE,  # e_phoff
        0,          # e_shoff
        0,          # e_flags
        EHDR_SIZE,  # e_ehsize
        PHDR_SIZE,  # e_phentsize
        1,          # e_phnum
        0, 0, 0,    # e_shentsize, e_shnum, e_shstrndx
    )

    phdr = struct.pack(
        "<IIQQQQQQ",
        1,          # p_type PT_LOAD
        5,          # p_flags R+X
        0,          # p_offset — map the whole file from 0
        0,          # p_vaddr — relative, loader adds the fixed image base
        0,          # p_paddr
        filesz,     # p_filesz
        filesz,     # p_memsz
        0x1000,     # p_align
    )

    return ehdr + phdr + CODE


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 1
    data = build()
    with open(sys.argv[1], "wb") as f:
        f.write(data)
    print(f"wrote {sys.argv[1]} ({len(data)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
