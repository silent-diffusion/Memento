#!/usr/bin/perl
# Apply the claymorphism treatment to the light artboards in project/.
# Idempotent: files already carrying the clay tokens are skipped.
use strict; use warnings; use utf8;
use File::Basename;
my $root = dirname(__FILE__) . "/project";

my $LIGHT = ";--danger:#B42318;--danger-soft:#F6E0DD"
 .";--clay-card:inset 4px 4px 10px rgba(255,255,255,.95),inset -4px -4px 10px rgba(29,28,26,.05),10px 14px 28px rgba(29,28,26,.10)"
 .";--clay-sm:inset 2px 2px 4px rgba(255,255,255,.95),inset -2px -2px 4px rgba(29,28,26,.07),4px 6px 12px rgba(29,28,26,.10)"
 .";--clay-xs:inset 1px 1px 2px rgba(255,255,255,.95),inset -1px -1px 2px rgba(29,28,26,.07),2px 3px 6px rgba(29,28,26,.08)"
 .";--clay-in:inset 3px 3px 7px rgba(29,28,26,.10),inset -2px -2px 6px rgba(255,255,255,.9)"
 .";--clay-accent:inset 3px 3px 6px rgba(255,255,255,.35),inset -3px -3px 6px rgba(0,0,0,.18),6px 10px 20px rgba(194,65,12,.30)"
 .";--clay-bar:0 6px 20px rgba(29,28,26,.06)";
my $DARK = ";--danger:#F28B82;--danger-soft:#3A1F1C"
 .";--clay-card:inset 3px 3px 8px rgba(255,255,255,.04),inset -4px -4px 10px rgba(0,0,0,.45),10px 14px 28px rgba(0,0,0,.45)"
 .";--clay-sm:inset 2px 2px 4px rgba(255,255,255,.05),inset -2px -2px 4px rgba(0,0,0,.45),4px 6px 12px rgba(0,0,0,.40)"
 .";--clay-xs:inset 1px 1px 2px rgba(255,255,255,.05),inset -1px -1px 2px rgba(0,0,0,.4),2px 3px 6px rgba(0,0,0,.35)"
 .";--clay-in:inset 3px 3px 7px rgba(0,0,0,.5),inset -2px -2px 6px rgba(255,255,255,.04)"
 .";--clay-accent:inset 3px 3px 6px rgba(255,255,255,.25),inset -3px -3px 6px rgba(0,0,0,.25),6px 10px 20px rgba(240,138,92,.25)"
 .";--clay-bar:0 6px 20px rgba(0,0,0,.4)";

my $CSS = <<'CSS';

/* claymorphism */
.pill{box-shadow:var(--clay-xs)}
.pill.queued,.pill.failed{box-shadow:none}
.chip.on,.nav.on,.icon-btn.on{background:var(--surface-2);color:var(--text);box-shadow:var(--clay-in)}
.seg{border-radius:12px}
.seg.on{background:var(--surface);color:var(--text);box-shadow:var(--clay-xs)}
.tog{box-shadow:var(--clay-in)}
.tog span{box-shadow:2px 3px 6px rgba(29,28,26,.25)}
.app.dark .tog span{box-shadow:2px 3px 6px rgba(0,0,0,.5)}
.primary{box-shadow:var(--clay-accent)}
.primary:hover{filter:none;transform:translateY(-1px)}
.ghost:hover,.icon-btn:hover,.pal:hover,.nav:hover{background:var(--surface-2)}
.mod{border-color:transparent;border-radius:20px;box-shadow:var(--clay-sm)}
.mod.on{border-color:transparent;box-shadow:var(--clay-sm),0 0 0 2px var(--text)}
.paper{border:none;border-radius:12px;box-shadow:var(--clay-card)}
.ver{border-color:transparent;border-radius:16px;box-shadow:var(--clay-sm)}
.ver.cur{box-shadow:var(--clay-sm),0 0 0 2px var(--text)}
.segm{border-radius:16px}
.segm.now{box-shadow:var(--clay-in)}
.chap{border-radius:12px}
.chap.on{box-shadow:var(--clay-in)}
.sw{box-shadow:var(--clay-xs)}
.sw.on{box-shadow:var(--clay-xs),0 0 0 2px var(--text)}
.drop{border:none;border-radius:24px;box-shadow:var(--clay-in)}
.drop.hot{box-shadow:var(--clay-in),0 0 0 2px var(--accent)}
.dz.live{border:none;box-shadow:var(--clay-in)}
.dz.live.hot{box-shadow:var(--clay-in),0 0 0 2px var(--accent)}
.side.live{border:none;box-shadow:var(--clay-in)}
.row.selected{box-shadow:var(--clay-in)}
.item{border-radius:14px}
.g{border:none!important;background:var(--surface)!important;box-shadow:var(--clay-sm);border-radius:14px!important}
.p{box-shadow:var(--clay-accent);border-radius:14px!important}
.d{box-shadow:inset 3px 3px 6px rgba(255,255,255,.3),inset -3px -3px 6px rgba(0,0,0,.2),6px 10px 20px rgba(180,35,24,.3);border-radius:14px!important}
.sw,.pal,.nav,.chip,.icon-btn,.ghost,.tog,.seg,.card,.row,.mod,.pill{transition:box-shadow .15s,transform .15s}
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
  if ($t =~ /--clay-card/) { print "skip  $n\n"; next; }

  $t =~ s/--focus:#2563EB/--focus:#2563EB$LIGHT/;
  $t =~ s/--focus:#60A5FA/--focus:#60A5FA$DARK/;

  # radii, largest first so nothing is remapped twice
  for my $pair (["16px","28px"],["14px","24px"],["12px","20px"],["10px","16px"],["9px","14px"],["8px","12px"],["7px","12px"]) {
    my ($a,$b) = @$pair;
    $t =~ s/border-radius: \Q$a\E/border-radius: $b/g;
  }

  # containers: drop the hairline, puff them up
  $t =~ s/border: 1px solid var\(--line\); border-radius: 28px/border-radius: 28px; box-shadow: var(--clay-card)/g;
  $t =~ s/border: 1px solid var\(--line\); border-radius: 24px/border-radius: 24px; box-shadow: var(--clay-card)/g;
  $t =~ s/border: 1px solid var\(--line\); border-radius: 20px/border-radius: 20px; box-shadow: var(--clay-sm)/g;
  $t =~ s/border: 1px solid var\(--line\); border-radius: 16px/border-radius: 16px; box-shadow: var(--clay-sm)/g;
  $t =~ s/border: 1px solid var\(--line\); border-radius: 14px/border-radius: 14px; box-shadow: var(--clay-sm)/g;
  $t =~ s/border: 1px solid var\(--line\); border-radius: 12px/border-radius: 12px; box-shadow: var(--clay-sm)/g;
  # inputs: pressed in
  $t =~ s/border: 1px solid var\(--line\); background: var\(--bg\)/border: none; background: var(--bg); box-shadow: var(--clay-in)/g;
  # ghost and icon buttons: small raised puffs
  $t =~ s/border: 1px solid var\(--line(?:-strong)?\); background: (?:transparent|var\(--surface\))/border: none; background: var(--surface); box-shadow: var(--clay-sm)/g;
  $t =~ s/border: 1px solid var\(--line(?:-strong)?\); display: inline-flex/border: none; background: var(--surface); box-shadow: var(--clay-sm); display: inline-flex/g;
  # segmented groups, tab strips, icon tiles: pressed-in wells
  $t =~ s/border-radius: (14|16|12)px; background: var\(--surface-2\)/border-radius: $1px; background: var(--surface-2); box-shadow: var(--clay-in)/g;
  # header bar: no hairline, soft shadow
  $t =~ s/border-bottom: 1px solid var\(--line\); background: var\(--surface\)/background: var(--surface); box-shadow: var(--clay-bar); position: relative; z-index: 1/g;
  # paper preview: shadow instead of border
  $t =~ s/\.paper\{background:#FFFFFF;color:#1D1C1A;border:1px solid var\(--line\);border-radius:4px;/.paper{background:#FFFFFF;color:#1D1C1A;/;
  # list dividers soften
  $t =~ s/\.row\+\.row\{border-top:1px solid var\(--line\)\}/.row+.row{border-top:1px solid var(--line)}/;
  # shared clay css
  $t =~ s/<\/style>\n<\/helmet>/$CSS<\/style>\n<\/helmet>/;
  # foundations copy
  $t =~ s/9 small · 10 control · 14 card · pill/14 small · 16 control · 24 card · pill/;
  $t =~ s/Manrope · JetBrains Mono · 4 px grid/Manrope · JetBrains Mono · clay surfaces · 4 px grid/;

  open(my $out, '>:encoding(UTF-8)', $p) or die "$p: $!";
  print $out $t; close $out;
  print "clay  $n\n";
}
