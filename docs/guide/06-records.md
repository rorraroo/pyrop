# 6. Records

A record is a type of your own that keeps several named values together, such as a point with its `x` and `y`, or a hero with a name and health.

## Declaring a record

`record` takes the record's name and then its fields, each a type and a name:

```pyrop
record Point float x float y end

define Point corner Point 3.0 4.0
write_line corner
```

```output
Point x 3.0 y 4.0
```

The record's name is also how you make one: `Point 3.0 4.0` takes one value per field, in order. Pyrop counts the fields, so it knows that `Point` takes two values. Record names start with a capital letter by convention, which keeps the lowercase word free for a variable, as in `define Point point …`.

When a record has many fields, it is easy to swap two values of the same type. Name each field with `with` instead, in any order:

```pyrop
record Hero int health int attack int gold end

define Hero hero Hero
    with attack 2
    with health 10
    with gold 0
write_line hero
```

```output
Hero health 10 attack 2 gold 0
```

Every field has to be there exactly once, so a forgotten or repeated field is an error before the program runs.

## Reading and changing fields

`the <field> of <record>` reads a field. `copy <record> with <field> <value>` gives a new record with one field changed:

```pyrop
record Hero string name int health end

define Hero hero Hero "Ann" 10
write_line the health of hero
set hero copy hero with health subtract the health of hero 3 end
write_line hero
```

```output
10
Hero name "Ann" health 7
```

Like lists, records never change; `copy … with` makes a new one and `set` stores it. One `copy` can change several fields, one `with` each:

```pyrop
record Point int x int y end

define Point spot Point 1 2
set spot copy spot with x 5 with y 6 end
write_line spot
```

```output
Point x 5 y 6
```

## Records inside records

A field can hold another record, or a list:

```pyrop
record Point float x float y end
record Hero string name Point position list string bag end

define Hero hero Hero "Ann" Point 0.0 0.0 list_of string "lamp" end
set hero copy hero with bag push the bag of hero "rope" end
write_line the x of the position of hero
write_line hero
```

```output
0.0
Hero name "Ann" position (Point x 0.0 y 0.0) bag ["lamp", "rope"]
```

`the x of the position of hero` reads from the inside out: the position of the hero, and then its `x`.

## Functions with records

Records make good parameters and results, because one value carries everything:

```pyrop
record Point float x float y end

function float distance Point a Point b block
    define float dx subtract the x of a the x of b
    define float dy subtract the y of a the y of b
    return from_square_root add multiply dx dx multiply dy dy
end

function float from_square_root float value block
    define float guess value
    define int round_number 0
    while less round_number 20 block
        set guess divide add guess divide value guess 2.0
        set round_number add round_number 1
    end
    return guess
end

write_line distance Point 0.0 0.0 Point 3.0 4.0
```

```output
5.0
```

`from_square_root` works out a square root by hand, to keep this chapter self-contained. Chapter 9 shows `square_root`, which comes ready-made in the library `math`.

## Comparing records

`equal` compares two records field by field:

```pyrop
record Point float x float y end
write_line equal Point 1.0 2.0 Point 1.0 2.0
```

```output
true
```

Next: [Choices and `match`](07-choices-and-match.md)
