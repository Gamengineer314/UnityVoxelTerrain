#define COLOR(r, g, b, variation) fixed4(r / 255.0, g / 255.0, b / 255.0, variation)

static const fixed4 colors[] = {
    // r, g, b,     color variation
    COLOR(255, 0, 0,        0),     // Error color
    COLOR(245, 245, 60,     0.1),   // Sand
    COLOR(15, 220, 0,       0.1),   // Grass
    COLOR(130, 130, 135,    0.06),  // Stone
    COLOR(235, 235, 235,    0.06)   // Snow
};

#define discretization 8