-- ==========================================
-- 1. GEOGRAPHY (Minimal Seed for Dummy Environment)
-- ==========================================

DO $$ 
DECLARE 
    v_country_id INT;
BEGIN
    INSERT INTO countries (name, code) 
    VALUES ('country', 'CTR')
    ON CONFLICT DO NOTHING;

    SELECT id INTO v_country_id FROM countries WHERE code = 'CTR';

    INSERT INTO regions (countryid, name) VALUES 
    (v_country_id, 'region')
    ON CONFLICT DO NOTHING;

    INSERT INTO cities (id, regionid, name) VALUES 
    ('c0000000-0000-0000-0000-000000000005', (SELECT id FROM regions WHERE name = 'region' AND countryid = v_country_id), 'city')
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Minimal geography seeding completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITIONS & CONFIGURATIONS
-- ==========================================

DO $$ 
DECLARE 
    -- Waterpolo IDs
    wp_sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
    wp_config_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70';
    wp_config_alt_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f71';
    
    /*
    -- Football IDs
    fb_sport_id uuid := '7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a6b';
    fb_config_id uuid := '7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a70';

    -- Basketball IDs
    bb_sport_id uuid := '8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b7c';
    bb_config_id uuid := '8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b80';
    */

BEGIN
    -- A. WATERPOLO SEEDING
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (wp_sport_id, 'Waterpolo', 'WP', wp_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', wp_sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', wp_sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', wp_sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', wp_sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', wp_sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', wp_sport_id, 'Utility', 'UTL')
    ON CONFLICT DO NOTHING;

    -- Standard Waterpolo Configuration (25x20 m)
    INSERT INTO sportconfigurations (
        id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit, playground, fieldlength, fieldwidth
    )
    VALUES (
        wp_config_id, 
        wp_sport_id, 
        true, 
        4, 
        8, 
        '25x20 sq.m.', 
        15, 
        13, 
        7, 
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 250"><defs><linearGradient id="poolWater" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#38bdf8"/><stop offset="50%" stop-color="#0284c7"/><stop offset="100%" stop-color="#38bdf8"/></linearGradient></defs><rect x="0" y="0" width="200" height="250" fill="url(#poolWater)" rx="4"/><rect x="0" y="0" width="200" height="250" fill="none" stroke="#0369a1" stroke-width="3"/><g id="opponent-goal"><rect x="85" y="-8" width="30" height="8" fill="#e0f2fe" fill-opacity="0.3" stroke="#ffffff" stroke-width="1.5"/><line x1="85" y1="0" x2="115" y2="0" stroke="#ef4444" stroke-width="2.5" stroke-linecap="round"/></g><g id="home-goal"><rect x="85" y="250" width="30" height="8" fill="#e0f2fe" fill-opacity="0.3" stroke="#ffffff" stroke-width="1.5"/><line x1="85" y1="250" x2="115" y2="250" stroke="#3b82f6" stroke-width="2.5" stroke-linecap="round"/></g><line x1="0" y1="125" x2="200" y2="125" stroke="#ffffff" stroke-width="2" stroke-dasharray="4 2" opacity="0.9"/><circle cx="100" cy="125" r="3" fill="#ffffff"/><line x1="0" y1="20" x2="200" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="65" y1="0" x2="65" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="135" y1="0" x2="135" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="50" x2="200" y2="50" stroke="#eab308" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="60" x2="200" y2="60" stroke="#22c55e" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="190" x2="200" y2="190" stroke="#22c55e" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="200" x2="200" y2="200" stroke="#eab308" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="230" x2="200" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="65" y1="250" x2="65" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="135" y1="250" x2="135" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><g id="side-markers" opacity="0.9"><rect x="0" y="18" width="4" height="4" fill="#ef4444"/><rect x="196" y="18" width="4" height="4" fill="#ef4444"/><rect x="0" y="48" width="4" height="4" fill="#eab308"/><rect x="196" y="48" width="4" height="4" fill="#eab308"/><rect x="0" y="58" width="4" height="4" fill="#22c55e"/><rect x="196" y="58" width="4" height="4" fill="#22c55e"/><rect x="0" y="123" width="4" height="4" fill="#ffffff"/><rect x="196" y="123" width="4" height="4" fill="#ffffff"/><rect x="0" y="188" width="4" height="4" fill="#22c55e"/><rect x="196" y="188" width="4" height="4" fill="#22c55e"/><rect x="0" y="198" width="4" height="4" fill="#eab308"/><rect x="196" y="198" width="4" height="4" fill="#eab308"/><rect x="0" y="228" width="4" height="4" fill="#ef4444"/><rect x="196" y="228" width="4" height="4" fill="#ef4444"/></g></svg>',
        25.00,
        20.00
    )
    ON CONFLICT DO NOTHING;

    -- Alternative Waterpolo Configuration (25x15 m)
    INSERT INTO sportconfigurations (
        id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit, playground, fieldlength, fieldwidth
    )
    VALUES (
        wp_config_alt_id, 
        wp_sport_id, 
        true, 
        4, 
        7, 
        '25x15 sq.m.', 
        13, 
        11, 
        6, 
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 150 250"><defs><linearGradient id="poolWaterAlt" x1="0%" y1="0%" x2="0%" y2="100%"><stop offset="0%" stop-color="#38bdf8"/><stop offset="50%" stop-color="#0284c7"/><stop offset="100%" stop-color="#38bdf8"/></linearGradient></defs><rect x="0" y="0" width="150" height="250" fill="url(#poolWaterAlt)" rx="4"/><rect x="0" y="0" width="150" height="250" fill="none" stroke="#0369a1" stroke-width="3"/><g id="opponent-goal"><rect x="60" y="-8" width="30" height="8" fill="#e0f2fe" fill-opacity="0.3" stroke="#ffffff" stroke-width="1.5"/><line x1="60" y1="0" x2="90" y2="0" stroke="#ef4444" stroke-width="2.5" stroke-linecap="round"/></g><g id="home-goal"><rect x="60" y="250" width="30" height="8" fill="#e0f2fe" fill-opacity="0.3" stroke="#ffffff" stroke-width="1.5"/><line x1="60" y1="250" x2="90" y2="250" stroke="#3b82f6" stroke-width="2.5" stroke-linecap="round"/></g><line x1="0" y1="125" x2="150" y2="125" stroke="#ffffff" stroke-width="2" stroke-dasharray="4 2" opacity="0.9"/><circle cx="75" cy="125" r="3" fill="#ffffff"/><line x1="0" y1="20" x2="150" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="40" y1="0" x2="40" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="110" y1="0" x2="110" y2="20" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="50" x2="150" y2="50" stroke="#eab308" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="60" x2="150" y2="60" stroke="#22c55e" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="190" x2="150" y2="190" stroke="#22c55e" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="200" x2="150" y2="200" stroke="#eab308" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="0" y1="230" x2="150" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="40" y1="250" x2="40" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><line x1="110" y1="250" x2="110" y2="230" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="3 2" opacity="0.8"/><g id="side-markers" opacity="0.9"><rect x="0" y="18" width="4" height="4" fill="#ef4444"/><rect x="146" y="18" width="4" height="4" fill="#ef4444"/><rect x="0" y="48" width="4" height="4" fill="#eab308"/><rect x="146" y="48" width="4" height="4" fill="#eab308"/><rect x="0" y="58" width="4" height="4" fill="#22c55e"/><rect x="146" y="58" width="4" height="4" fill="#22c55e"/><rect x="0" y="123" width="4" height="4" fill="#ffffff"/><rect x="146" y="123" width="4" height="4" fill="#ffffff"/><rect x="0" y="188" width="4" height="4" fill="#22c55e"/><rect x="146" y="188" width="4" height="4" fill="#22c55e"/><rect x="0" y="198" width="4" height="4" fill="#eab308"/><rect x="146" y="198" width="4" height="4" fill="#eab308"/><rect x="0" y="228" width="4" height="4" fill="#ef4444"/><rect x="146" y="228" width="4" height="4" fill="#ef4444"/></g></svg>',
        25.00,
        15.00
    )
    ON CONFLICT DO NOTHING;

    -- Waterpolo Event Definitions
    INSERT INTO eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, issoftdeleted, createdat) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f51', wp_sport_id, NULL, 'Goal', 'GOAL+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f52', wp_sport_id, NULL, 'Assist', 'ASST+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f53', wp_sport_id, NULL, 'Sprint', 'SPR+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f54', wp_sport_id, NULL, 'Exclusion', 'EXCL+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f55', wp_sport_id, NULL, 'Penalty', 'PEN+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f56', wp_sport_id, NULL, 'Steal', 'STL+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f57', wp_sport_id, NULL, 'Shot Saved', 'SAVE+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f58', wp_sport_id, NULL, 'Block', 'BLOK', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f59', wp_sport_id, NULL, 'Turnover', 'T-OVER+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f60', wp_sport_id, NULL, 'Defensive Transition', 'D-TRANS+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', wp_sport_id, NULL, 'Offensive Transition', 'O-TRANS+', true, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', wp_sport_id, NULL, 'Shot Missed', 'MISS-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', wp_sport_id, NULL, 'Turnover', 'T-OVER-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', wp_sport_id, NULL, 'Exclusion', 'EXCL-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', wp_sport_id, NULL, 'Penalty', 'PEN-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', wp_sport_id, NULL, 'Sprint', 'SPR-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f67', wp_sport_id, NULL, 'Critical Foul', 'C-FOUL-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f68', wp_sport_id, NULL, 'Blocked shot', 'B-SHOT-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f69', wp_sport_id, NULL, 'Tactical Error', 'T-ERR-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70', wp_sport_id, NULL, 'Defensive Transition', 'D-TRANS-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f71', wp_sport_id, NULL, 'Offensive Transition', 'O-TRANS-', false, false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f72', wp_sport_id, NULL, 'Steal', 'STL-', false, false, NOW())
    ON CONFLICT DO NOTHING;

    /*
    -- B. FOOTBALL SEEDING
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (fb_sport_id, 'Football', 'FB', fb_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a61', fb_sport_id, 'Goalkeeper', 'GK'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a62', fb_sport_id, 'Defender', 'DEF'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a63', fb_sport_id, 'Midfielder', 'MID'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a64', fb_sport_id, 'Forward', 'FWD')
    ON CONFLICT DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (fb_config_id, fb_sport_id, false, 2, 45, '105x68 sq.m.', 23, 18, 11)
    ON CONFLICT DO NOTHING;

    INSERT INTO eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, issoftdeleted, createdat) VALUES 
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a81', fb_sport_id, NULL, 'Goal', 'GOAL', true, false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a82', fb_sport_id, NULL, 'Assist', 'ASST', true, false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a83', fb_sport_id, NULL, 'Key Pass', 'KP', true, false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a84', fb_sport_id, NULL, 'Yellow Card', 'YC', false, false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a85', fb_sport_id, NULL, 'Red Card', 'RC', false, false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a86', fb_sport_id, NULL, 'Foul Committed', 'FOUL', false, false, NOW())
    ON CONFLICT DO NOTHING;

    -- C. BASKETBALL SEEDING
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (bb_sport_id, 'Basketball', 'BB', bb_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b61', bb_sport_id, 'Point Guard', 'PG'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b62', bb_sport_id, 'Shooting Guard', 'SG'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b63', bb_sport_id, 'Small Forward', 'SF'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b64', bb_sport_id, 'Power Forward', 'PF'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b65', bb_sport_id, 'Center', 'C')
    ON CONFLICT DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (bb_config_id, bb_sport_id, true, 4, 10, '28x15 sq.m.', 12, 12, 5)
    ON CONFLICT DO NOTHING;

    INSERT INTO eventdefinitions (id, sportid, ownerid, name, shortname, ispositive, issoftdeleted, createdat) VALUES 
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b81', bb_sport_id, NULL, 'Field Goal', 'FG', true, false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b82', bb_sport_id, NULL, 'Three-Pointer', '3PT', true, false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b83', bb_sport_id, NULL, 'Assist', 'AST', true, false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b84', bb_sport_id, NULL, 'Rebound', 'REB', true, false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b85', bb_sport_id, NULL, 'Turnover', 'TO', false, false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b86', bb_sport_id, NULL, 'Personal Foul', 'PF', false, false, NOW())
    ON CONFLICT DO NOTHING;
    */

END $$;

-- ==========================================
-- 3. BASE CLUB (TTA Training Club)
-- ==========================================

INSERT INTO clubs (id, cityid, name, createdat) VALUES 
('11111111-1111-1111-1111-000000000001', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'city' AND r.name = 'region' AND co.code = 'CTR' LIMIT 1), 'TTA Training Club', NOW())
ON CONFLICT DO NOTHING;

-- ==========================================
-- 4. USERS (Without pre-assigned clubs/teams)
-- ==========================================

DO $$ 
DECLARE 
    v_user_id VARCHAR := 'auth0|69cf7ec5eff8f1358a0b9ae0'; 
    v_user_id_2 VARCHAR := 'auth0|698b956080889e5401cef7c5'; 
    v_user_id_3 VARCHAR := 'auth0|698b9bd69f764e2999518960';
BEGIN
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id, 'hlib.bondarev@gmail.com', 'Hlib Bondarev', NOW())
    ON CONFLICT DO NOTHING;

    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_2, 'user1@example.com', 'Taras Shevchenko', NOW())
    ON CONFLICT DO NOTHING;

    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_3, 'user2@example.com', 'Ivan Franko', NOW())
    ON CONFLICT DO NOTHING;

    -- Assign Global FullControl (Admin) policy to v_user_id (Hlib Bondarev)
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999904', v_user_id, 0, 0, NULL, NOW()
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id AND targettype = 0
    );

END $$;

-- ======================================================
-- 5. PLAYERS (100 Home + 100 Guest Static Club Players)
-- ======================================================

DO $$ 
DECLARE 
    v_tta_club_id uuid := '11111111-1111-1111-1111-000000000001';
BEGIN
    -- 100 Home Players
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
    SELECT 
        ('44444444-4444-4444-1111-' || LPAD(i::text, 12, '0'))::UUID AS id,
        v_tta_club_id AS homeclubid,
        'Home Player' AS firstname,
        i::text AS lastname,
        '2011-01-01'::DATE AS birthdate,
        0 AS gender,
        NOW() AS createdat
    FROM generate_series(1, 100) AS i
    ON CONFLICT DO NOTHING;

    -- 100 Guest Players
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
    SELECT 
        ('44444444-4444-4444-2222-' || LPAD(i::text, 12, '0'))::UUID AS id,
        v_tta_club_id AS homeclubid,
        'Guest Player' AS firstname,
        i::text AS lastname,
        '2011-01-01'::DATE AS birthdate,
        0 AS gender,
        NOW() AS createdat
    FROM generate_series(1, 100) AS i
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Seed for 100 Home and 100 Guest static players in TTA Training Club completed successfully.';
END $$;