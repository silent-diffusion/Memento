#!/usr/bin/perl
# Neumorphism pass: runs on files that already carry the clay treatment.
# Same palette family, but surfaces are extruded from one matte ground with
# paired light/dark shadows, and controls depress on press.
use strict; use warnings; use utf8;
use File::Basename;
my $root = dirname(__FILE__) . "/project";

my $NEO_LIGHT = ";--neo-card:-8px -8px 18px rgba(255,255,255,.9),10px 10px 22px rgba(29,28,26,.14)"
 .";--neo-sm:-4px -4px 10px rgba(255,255,255,.9),5px 5px 12px rgba(29,28,26,.14)"
 .";--neo-sm-hi:-6px -6px 14px rgba(255,255,255,.95),7px 7px 16px rgba(29,28,26,.17)"
 .";--neo-xs:-2px -2px 5px rgba(255,255,255,.9),3px 3px 6px rgba(29,28,26,.12)"
 .";--neo-in:inset 4px 4px 9px rgba(29,28,26,.13),inset -4px -4px 9px rgba(255,255,255,.9)"
 .";--neo-in-xs:inset 2px 2px 4px rgba(29,28,26,.14),inset -2px -2px 4px rgba(255,255,255,.9)"
 .";--neo-accent:-4px -4px 10px rgba(255,255,255,.7),6px 6px 14px rgba(194,65,12,.35)"
 .";--neo-accent-in:inset 4px 4px 8px rgba(0,0,0,.25),inset -3px -3px 8px rgba(255,255,255,.25)"
 .";--neo-bar:0 8px 18px rgba(29,28,26,.08)"
 .";--accent-grad:linear-gradient(145deg,#D04E18,#B03A0A)"
 .";--danger-grad:linear-gradient(145deg,#C43024,#9E1C12)"
 .";--knob:linear-gradient(145deg,#FFFFFF,#E4E1DA)";
my $NEO_DARK = ";--neo-card:-8px -8px 18px rgba(255,255,255,.035),10px 10px 22px rgba(0,0,0,.6)"
 .";--neo-sm:-4px -4px 10px rgba(255,255,255,.04),5px 5px 12px rgba(0,0,0,.6)"
 .";--neo-sm-hi:-6px -6px 14px rgba(255,255,255,.05),7px 7px 16px rgba(0,0,0,.65)"
 .";--neo-xs:-2px -2px 5px rgba(255,255,255,.04),3px 3px 6px rgba(0,0,0,.55)"
 .";--neo-in:inset 4px 4px 9px rgba(0,0,0,.6),inset -4px -4px 9px rgba(255,255,255,.04)"
 .";--neo-in-xs:inset 2px 2px 4px rgba(0,0,0,.6),inset -2px -2px 4px rgba(255,255,255,.04)"
 .";--neo-accent:-4px -4px 10px rgba(255,255,255,.04),6px 6px 14px rgba(240,138,92,.3)"
 .";--neo-accent-in:inset 4px 4px 8px rgba(0,0,0,.4),inset -3px -3px 8px rgba(255,255,255,.15)"
 .";--neo-bar:0 8px 18px rgba(0,0,0,.5)"
 .";--accent-grad:linear-gradient(145deg,#F79A70,#E07848)"
 .";--danger-grad:linear-gradient(145deg,#F6A09A,#D9655C)"
 .";--knob:linear-gradient(145deg,#34322E,#242320)";

my $CSS = <<'CSS';
/* neumorphism: one matte ground, paired light/dark shadows, controls that depress */
.pill{box-shadow:var(--neo-xs)}
.pill.done{background:var(--surface)}
.pill.queued,.pill.failed{box-shadow:none}
.chip.on,.nav.on,.icon-btn.on{background:var(--surface);color:var(--text);box-shadow:var(--neo-in-xs)}
.seg{border-radius:12px;transition:box-shadow .12s,transform .12s,background .12s}
.seg.on{background:var(--surface);color:var(--text);box-shadow:var(--neo-xs)}
.tog{box-shadow:var(--neo-in-xs);background:var(--surface-2);transition:background .18s}
.tog.on{background:var(--text)}
.tog span{background:var(--knob);box-shadow:2px 3px 6px rgba(29,28,26,.28),-1px -1px 2px rgba(255,255,255,.8);transition:left .22s cubic-bezier(.34,1.56,.64,1),width .1s}
.tog.on span{background:var(--knob)}
.app.dark .tog span{box-shadow:2px 3px 6px rgba(0,0,0,.6),-1px -1px 2px rgba(255,255,255,.08)}
.tog:active span{width:20px}
.tog.on:active span{left:17px}
.primary{box-shadow:var(--neo-accent)}
.primary:hover{filter:none;box-shadow:var(--neo-accent),-6px -6px 14px rgba(255,255,255,.5)}
.primary:active{box-shadow:var(--neo-accent-in)!important;transform:translateY(1px) scale(.985)}
.ghost:hover,.icon-btn:hover,.pal:hover,.nav:hover,.chip:hover,.card:hover{background:var(--surface);box-shadow:var(--neo-sm-hi)}
.ghost:active,.icon-btn:active,.pal:active,.nav:active,.chip:active,.card:active,.seg:active,.sw:active,.modhead:active,.chap:active,.segm:active{box-shadow:var(--neo-in)!important;transform:translateY(1px) scale(.985)}
.chip.on:hover,.nav.on:hover,.icon-btn.on:hover{box-shadow:var(--neo-in-xs)}
.row:hover{background:var(--surface);box-shadow:var(--neo-in-xs)}
.row:active{box-shadow:var(--neo-in)!important}
.row.selected{box-shadow:var(--neo-in)}
.mod{border-color:transparent;border-radius:20px;box-shadow:var(--neo-sm)}
.mod:hover{box-shadow:var(--neo-sm-hi)}
.mod.on{border-color:transparent;box-shadow:var(--neo-sm),0 0 0 2px var(--text)}
.paper{border:none;border-radius:12px;box-shadow:var(--neo-card)}
.ver{border-color:transparent;border-radius:16px;box-shadow:var(--neo-sm)}
.ver.cur{box-shadow:var(--neo-sm),0 0 0 2px var(--text)}
.segm{border-radius:16px}
.segm:hover{background:var(--surface);box-shadow:var(--neo-in-xs)}
.segm.now{background:var(--surface);box-shadow:var(--neo-in)}
.chap{border-radius:12px}
.chap:hover{background:var(--surface);box-shadow:var(--neo-in-xs)}
.chap.on{background:var(--surface);box-shadow:var(--neo-in)}
.sw{box-shadow:var(--neo-xs)}
.sw.on{box-shadow:var(--neo-xs),0 0 0 2px var(--text)}
.drop{border:none;border-radius:24px;box-shadow:var(--neo-in)}
.drop.hot{box-shadow:var(--neo-in),0 0 0 2px var(--accent)}
.dz.live{border:none;box-shadow:var(--neo-in)}
.dz.live.hot{box-shadow:var(--neo-in),0 0 0 2px var(--accent)}
.side.live{border:none;box-shadow:var(--neo-in)}
.item{border-radius:14px}
.item:hover{background:var(--surface);box-shadow:var(--neo-in-xs)}
.g{border:none!important;background:var(--surface)!important;box-shadow:var(--neo-sm);border-radius:14px!important}
.g:active{box-shadow:var(--neo-in)!important;transform:translateY(1px) scale(.985)}
.p{background:var(--accent-grad)!important;box-shadow:var(--neo-accent);border-radius:14px!important}
.p:active{box-shadow:var(--neo-accent-in)!important;transform:translateY(1px) scale(.985)}
.d{background:var(--danger-grad)!important;box-shadow:-4px -4px 10px rgba(255,255,255,.7),6px 6px 14px rgba(180,35,24,.35);border-radius:14px!important}
.d:active{box-shadow:var(--neo-accent-in)!important;transform:translateY(1px) scale(.985)}
.chk{accent-color:var(--text);filter:drop-shadow(1px 1px 1px rgba(29,28,26,.2))}
.bar.live,.wb.played{background:var(--ok)}
.search,.field,.input{transition:box-shadow .15s}
.search:focus,.field:focus,.input:focus{box-shadow:var(--neo-in),0 0 0 2px var(--focus)!important;outline:none}
.sw,.pal,.nav,.chip,.icon-btn,.ghost,.btn,.seg,.card,.row,.mod,.pill,.modhead,.chap,.segm,.item{transition:box-shadow .15s ease,transform .08s ease,background .15s ease}
.btn:active,.icon-btn:active{transition-duration:.04s}
CSS

my @files = @ARGV;
unless (@files) {
  opendir(my $dh, $root) or die $!;
  @files = sort grep { /\.dc\.html$/ && !/Dark\.dc\.html$/ } readdir($dh);
  closedir $dh;
}

for my $n (@files) {
  my $p = "$root/$n";
  open(my $fh, '<:encoding(UTF-8)', $p) or die "$p: $!";
  local $/; my $t = <$fh>; close $fh;
  if ($t !~ /--clay-card/) { print "skip  $n (no clay pass)\n"; next; }

  # one matte ground: surface == bg, wells a shade darker, hairlines become grooves
  $t =~ s/--bg:#F5F4F0;--surface:#FFFFFF;--surface-2:#EDEBE6;--line:#E2DFD8/--bg:#EFEDE8;--surface:#EFEDE8;--surface-2:#E4E1DA;--line:rgba(29,28,26,.07)/;
  $t =~ s/--bg:#161513;--surface:#1E1D1A;--surface-2:#272623;--line:#2E2C28/--bg:#1F1E1B;--surface:#1F1E1B;--surface-2:#1A1917;--line:rgba(255,255,255,.06)/;
  # swap the shadow tokens
  $t =~ s/(--danger-soft:#F6E0DD);--clay-card:.*?--clay-bar:[^;}]*/$1$NEO_LIGHT/;
  $t =~ s/(--danger-soft:#3A1F1C);--clay-card:.*?--clay-bar:[^;}]*/$1$NEO_DARK/;
  # every reference follows the rename
  $t =~ s/clay-/neo-/g;
  # primary buttons get a convex fill
  $t =~ s/(<(?:a|button) class="btn primary"[^>]*?)background: var\(--accent\)/$1background: var(--accent-grad)/g;
  # the shared css block
  $t =~ s/\/\* claymorphism \*\/.*?(?=<\/style>)/$CSS/s;
  # foundations copy and swatches
  $t =~ s/clay surfaces/neumorphic surfaces/;
  $t =~ s/'#F5F4F0', '#FFFFFF', '#EDEBE6', '#E2DFD8'/'#EFEDE8', '#EFEDE8', '#E4E1DA', 'rgba(29,28,26,.07)'/;
  $t =~ s/'#161513', '#1E1D1A', '#272623', '#2E2C28'/'#1F1E1B', '#1F1E1B', '#1A1917', 'rgba(255,255,255,.06)'/;

  open(my $out, '>:encoding(UTF-8)', $p) or die "$p: $!";
  print $out $t; close $out;
  print "neo   $n\n";
}
