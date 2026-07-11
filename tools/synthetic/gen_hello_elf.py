# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later

#!/usr/bin/env python3
"""Generate a synthetic PS5 "hello world" ELF that exercises SharpEmu's HLE path.

The image imports two symbols by their Sony NID (printf = hcuQgD53UxM,
exit = uMei1W9uyNo), calls printf with a message, then exit(0). This proves
the full loader -> relocation -> import stub -> HLE dispatch chain, unlike
gen_min_elf.py which only proves native execution + clean return.

Layout (file offset == vaddr, loader rebases everything to 0x800000000):
  0x0000 ELF header + 3 program headers (LOAD RX, LOAD RW, DYNAMIC)
  0x0100 code
  0x1000 message string
  0x1080 GOT slots (patched by the loader to import stubs)
  0x1100 dynsym (null, printf, exit)
  0x1180 dynstr
  0x1200 rela (2x R_X86_64_JMP_SLOT)
  0x1800 dynamic table

Usage: gen_hello_elf.py [--no-exit] <output.elf>
  --no-exit: skip the exit(0) import call and return from the entry point
             instead (isolates the emulator's exit-import handling).
"""

import hashlib
import struct
import sys

EHDR_SIZE = 64
PHDR_SIZE = 56

NID_SALT = bytes.fromhex("518D64A635DED8C1E6B039B1C3E55230")


def name2nid(name: str) -> str:
    """Sony NID: base64 of the byte-reversed first 8 bytes of SHA1(name + salt),
    +,- alphabet, no padding. Matches scripts/generate_aerolib_binary.py, which
    reads the digest as little-endian u64 and re-serializes it big-endian."""
    import base64
    digest = hashlib.sha1(name.encode() + NID_SALT).digest()[:8][::-1]
    return base64.b64encode(digest, altchars=b"+-").decode().rstrip("=")


CODE_VADDR = 0x100
MSG_VADDR = 0x1000
GOT_VADDR = 0x1080
DYNSYM_VADDR = 0x1100
DYNSTR_VADDR = 0x1180
RELA_VADDR = 0x1200
DYNAMIC_VADDR = 0x1800

MESSAGE = b"Hello from a synthetic PS5 ELF via SharpEmu HLE!\n\x00"


def rel32(target: int, next_ip: int) -> bytes:
    return struct.pack("<i", target - next_ip)


def build_code(call_exit: bool) -> bytes:
    code = b""
    ip = CODE_VADDR
    # lea rdi, [rip+msg]
    ins = b"\x48\x8d\x3d" + rel32(MSG_VADDR, ip + 7)
    code += ins; ip += len(ins)
    # xor eax, eax (al=0: no vector varargs)
    code += b"\x31\xc0"; ip += 2
    # call [rip+got.printf]
    ins = b"\xff\x15" + rel32(GOT_VADDR, ip + 6)
    code += ins; ip += len(ins)
    if call_exit:
        # xor edi, edi
        code += b"\x31\xff"; ip += 2
        # call [rip+got.exit]
        ins = b"\xff\x15" + rel32(GOT_VADDR + 8, ip + 6)
        code += ins; ip += len(ins)
    # clean exit via the return-to-host sentinel: xor eax,eax ; ret
    code += b"\x31\xc0\xc3"
    return code


def build(call_exit: bool = True) -> bytes:
    printf_nid = name2nid("printf")   # hcuQgD53UxM
    exit_nid = name2nid("exit")       # uMei1W9uyNo

    dynstr = b"\x00" + printf_nid.encode() + b"\x00" + exit_nid.encode() + b"\x00"
    printf_nameoff = 1
    exit_nameoff = 1 + len(printf_nid) + 1

    def sym(nameoff: int) -> bytes:
        # st_name, st_info=GLOBAL|FUNC, st_other, st_shndx=UND, st_value, st_size
        return struct.pack("<IBBHQQ", nameoff, 0x12, 0, 0, 0, 0)

    dynsym = struct.pack("<IBBHQQ", 0, 0, 0, 0, 0, 0) + sym(printf_nameoff) + sym(exit_nameoff)

    def rela(offset: int, symidx: int) -> bytes:
        return struct.pack("<QQq", offset, (symidx << 32) | 7, 0)  # R_X86_64_JMP_SLOT

    relas = rela(GOT_VADDR, 1) + rela(GOT_VADDR + 8, 2)

    def dyn(tag: int, val: int) -> bytes:
        return struct.pack("<qQ", tag, val)

    dynamic = (
        dyn(0x05, DYNSTR_VADDR)       # DT_STRTAB
        + dyn(0x0A, len(dynstr))      # DT_STRSZ
        + dyn(0x06, DYNSYM_VADDR)     # DT_SYMTAB
        + dyn(0x0B, 24)               # DT_SYMENT
        + dyn(0x17, RELA_VADDR)       # DT_JMPREL
        + dyn(0x02, len(relas))       # DT_PLTRELSZ
        + dyn(0x14, 7)                # DT_PLTREL = RELA
        + dyn(0x00, 0)                # DT_NULL
    )

    # --- assemble file image: offset == vaddr for simplicity ---
    image = bytearray(DYNAMIC_VADDR + len(dynamic))

    def place(vaddr: int, blob: bytes) -> None:
        image[vaddr:vaddr + len(blob)] = blob

    place(CODE_VADDR, build_code(call_exit))
    place(MSG_VADDR, MESSAGE)
    # GOT slots stay zero; the loader patches them via the JMP_SLOT relocations.
    place(DYNSYM_VADDR, dynsym)
    place(DYNSTR_VADDR, dynstr)
    place(RELA_VADDR, relas)
    place(DYNAMIC_VADDR, dynamic)

    filesz = len(image)

    e_ident = bytes([0x7F, ord("E"), ord("L"), ord("F"), 2, 1, 1, 9, 2]) + bytes(7)
    ehdr = struct.pack(
        "<16sHHIQQQIHHHHHH",
        e_ident,
        2, 62, 1,
        CODE_VADDR,       # e_entry (relative; loader adds 0x800000000)
        EHDR_SIZE, 0, 0,
        EHDR_SIZE, PHDR_SIZE,
        3,                # e_phnum
        0, 0, 0,
    )

    def phdr(ptype: int, flags: int, off: int, vaddr: int, size: int) -> bytes:
        return struct.pack("<IIQQQQQQ", ptype, flags, off, vaddr, vaddr, size, size, 0x1000)

    phdrs = (
        phdr(1, 5, 0, 0, 0x1000)                              # PT_LOAD R+X: headers+code
        + phdr(1, 6, 0x1000, 0x1000, filesz - 0x1000)         # PT_LOAD R+W: data/got/tables
        + phdr(2, 4, DYNAMIC_VADDR, DYNAMIC_VADDR, len(dynamic))  # PT_DYNAMIC
    )

    place(0, ehdr)
    place(EHDR_SIZE, phdrs)
    return bytes(image)


def main() -> int:
    args = sys.argv[1:]
    call_exit = True
    if args and args[0] == "--no-exit":
        call_exit = False
        args = args[1:]
    if len(args) != 1:
        print(__doc__.strip(), file=sys.stderr)
        return 1
    data = build(call_exit)
    with open(args[0], "wb") as f:
        f.write(data)
    print(f"wrote {args[0]} ({len(data)} bytes, exit={'yes' if call_exit else 'no'}), "
          f"printf={name2nid('printf')} exit={name2nid('exit')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
