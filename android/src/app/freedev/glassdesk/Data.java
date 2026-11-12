package app.freedev.glassdesk;

/** Everything a widget needs to draw itself. Plain Java so previews can fill it too. */
public final class Data {
    public long now = System.currentTimeMillis();
    public boolean preview;
    public boolean h24;

    public int batt = -1;
    public boolean charging;
    public long memUsed, memTotal, diskUsed, diskTotal;

    public String note = "";

    public String[] todo = new String[0];
    public boolean[] done = new boolean[0];

    public String city;          // null = no city set
    public boolean wxOk;         // forecast loaded
    public boolean isDay = true;
    public int temp, code = -1, hi, lo;
    public String[] dDay = new String[0];
    public int[] dCode = new int[0], dHi = new int[0], dLo = new int[0];

    public int quote;

    public String cdTitle = "";
    public long cdDate = -1;     // millis at local midnight, -1 = not set

    public String fenceName = "Apps";
    public Object[] icons = new Object[0];
    public String[] labels = new String[0];
}
