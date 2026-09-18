package com.tokendock.app.ui.ios

import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * HyperOS 3 风格组件套件（不依赖 Material 外观）：
 * 轻盈圆润的卡片与胶囊、圆形图标按钮、分段控件、分组列表、胶囊进度条。
 * 颜色由 [LocalIsDark] 驱动（浅色雾白 / 暗色深灰，均不使用纯白与纯黑）。
 */

val LocalIsDark = compositionLocalOf { false }

/** HyperOS 语义色板。 */
data class IosColors(
    val background: Color,
    val card: Color,
    val cardBorder: Color,
    val separator: Color,
    val label: Color,
    val secondaryLabel: Color,
    val tertiaryLabel: Color,
    val fill: Color,
    val accent: Color,
    val green: Color,
    val amber: Color,
    val red: Color,
)

private val LightPalette = IosColors(
    // 柔和雾白背景（不用纯白），卡片近白
    background = Color(0xFFF3F5F8),
    card = Color(0xFFFFFFFF),
    cardBorder = Color(0x0F0F172A),
    separator = Color(0x140F172A),
    label = Color(0xFF14171C),
    secondaryLabel = Color(0x99272D38),
    tertiaryLabel = Color(0x4D272D38),
    fill = Color(0x0F0F172A),
    accent = Color(0xFF0E7490),
    green = Color(0xFF16A34A),
    amber = Color(0xFFEA580C),
    red = Color(0xFFDC2626),
)

private val DarkPalette = IosColors(
    // 深灰层次（不用纯黑）
    background = Color(0xFF17181B),
    card = Color(0xFF212327),
    cardBorder = Color(0x14FFFFFF),
    separator = Color(0x1AFFFFFF),
    label = Color(0xFFECEEF2),
    secondaryLabel = Color(0x99A8B0BC),
    tertiaryLabel = Color(0x4DA8B0BC),
    fill = Color(0x1FA8B0BC),
    accent = Color(0xFF22D3EE),
    green = Color(0xFF34D399),
    amber = Color(0xFFFB923C),
    red = Color(0xFFF87171),
)

@Composable
fun iosColors(): IosColors = if (LocalIsDark.current) DarkPalette else LightPalette

@Composable
fun ProvideIosTheme(dark: Boolean, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalIsDark provides dark) { content() }
}

// ---------- 卡片与分组 ----------

/** 主卡片：22dp 圆角、轻阴影、弱边框（HyperOS 的轻盈层次）。 */
@Composable
fun IosCard(
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    val colors = iosColors()
    Column(
        modifier = modifier
            .fillMaxWidth()
            .shadow(
                elevation = if (LocalIsDark.current) 0.dp else 3.dp,
                shape = RoundedCornerShape(22.dp),
                ambientColor = Color(0x14000000),
                spotColor = Color(0x14000000),
            )
            .clip(RoundedCornerShape(22.dp))
            .background(colors.card)
            .border(1.dp, colors.cardBorder, RoundedCornerShape(22.dp))
            .then(
                if (onClick != null) {
                    Modifier.clickable(
                        interactionSource = remember { MutableInteractionSource() },
                        indication = null,
                        onClick = onClick,
                    )
                } else {
                    Modifier
                },
            )
            .padding(18.dp),
        content = content,
    )
}

/** 分组容器（设置页）：22dp 圆角卡片，内部行之间用发丝线分隔。 */
@Composable
fun IosSection(content: @Composable ColumnScope.() -> Unit) {
    val colors = iosColors()
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 20.dp)
            .shadow(
                elevation = if (LocalIsDark.current) 0.dp else 3.dp,
                shape = RoundedCornerShape(22.dp),
                ambientColor = Color(0x14000000),
                spotColor = Color(0x14000000),
            )
            .clip(RoundedCornerShape(22.dp))
            .background(colors.card)
            .border(1.dp, colors.cardBorder, RoundedCornerShape(22.dp)),
        content = content,
    )
}

/** 分组标题（HyperOS：小号、次级灰、缩进与卡片一致）。 */
@Composable
fun IosSectionHeader(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        fontWeight = FontWeight.Medium,
        color = iosColors().secondaryLabel,
        modifier = Modifier.padding(start = 32.dp, top = 22.dp, bottom = 8.dp),
    )
}

/** 行间发丝分隔线（左侧内缩）。 */
@Composable
fun IosSeparator(startInset: Dp = 18.dp) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(start = startInset)
            .height(1.dp)
            .background(iosColors().separator),
    )
}

/** 分组页脚说明。 */
@Composable
fun IosFooter(text: String) {
    Text(
        text = text,
        fontSize = 13.sp,
        color = iosColors().secondaryLabel,
        modifier = Modifier.padding(start = 32.dp, end = 32.dp, top = 8.dp),
    )
}

/** 通用行：可选图标块 + 标题/副标题 + 右侧内容 + chevron。 */
@Composable
fun IosRow(
    title: String,
    modifier: Modifier = Modifier,
    subtitle: String? = null,
    icon: ImageVector? = null,
    iconTint: Color = iosColors().accent,
    trailingText: String? = null,
    trailing: (@Composable () -> Unit)? = null,
    showChevron: Boolean = false,
    onClick: (() -> Unit)? = null,
) {
    val colors = iosColors()
    Row(
        modifier = modifier
            .fillMaxWidth()
            .then(
                if (onClick != null) {
                    Modifier.clickable(
                        interactionSource = remember { MutableInteractionSource() },
                        indication = null,
                        onClick = onClick,
                    )
                } else {
                    Modifier
                },
            )
            .padding(horizontal = 18.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            Box(
                modifier = Modifier
                    .size(30.dp)
                    .clip(RoundedCornerShape(9.dp))
                    .background(iconTint.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(icon, contentDescription = null, tint = iconTint, modifier = Modifier.size(18.dp))
            }
            Spacer(Modifier.width(12.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 16.sp, color = colors.label)
            if (subtitle != null) {
                Text(subtitle, fontSize = 13.sp, color = colors.secondaryLabel)
            }
        }
        if (trailingText != null) {
            Text(trailingText, fontSize = 16.sp, color = colors.secondaryLabel)
        }
        trailing?.invoke()
        if (showChevron) {
            Spacer(Modifier.width(4.dp))
            Text("›", fontSize = 20.sp, color = colors.tertiaryLabel)
        }
    }
}

/** 行内内容块（分段控件、滑块等）。 */
@Composable
fun IosRowBlock(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxWidth().padding(horizontal = 18.dp, vertical = 12.dp)) { content() }
}

// ---------- 圆形图标按钮（HyperOS 顶部操作用） ----------

@Composable
fun IosCircleIconButton(
    icon: ImageVector,
    contentDescription: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = iosColors()
    Box(
        modifier = modifier
            .size(40.dp)
            .clip(CircleShape)
            .background(colors.fill)
            .clickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null,
                onClick = onClick,
            ),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, contentDescription = contentDescription, tint = colors.accent, modifier = Modifier.size(20.dp))
    }
}

// ---------- 开关 ----------

@Composable
fun IosSwitch(
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    enabled: Boolean = true,
) {
    val colors = iosColors()
    val trackWidth = 50.dp
    val trackHeight = 30.dp
    val thumbSize = 26.dp
    val offset by animateDpAsState(
        targetValue = if (checked) trackWidth - thumbSize - 2.dp else 2.dp,
        animationSpec = tween(200),
        label = "switchThumb",
    )
    val track by animateColorAsState(
        targetValue = when {
            !enabled -> colors.fill
            checked -> colors.green
            else -> colors.fill
        },
        label = "switchTrack",
    )

    Box(
        modifier = Modifier
            .size(trackWidth, trackHeight)
            .clip(CircleShape)
            .background(track)
            .clickable(enabled = enabled) { onCheckedChange(!checked) },
        contentAlignment = Alignment.CenterStart,
    ) {
        Box(
            modifier = Modifier
                .offset(x = offset)
                .size(thumbSize)
                .shadow(1.dp, CircleShape)
                .clip(CircleShape)
                .background(Color.White),
        )
    }
}

// ---------- 分段控件（HyperOS 胶囊分段，指示块按宽度比例滑动） ----------

@Composable
fun IosSegmentedControl(
    options: List<String>,
    selectedIndex: Int,
    onSelected: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = iosColors()

    BoxWithConstraints(
        modifier = modifier
            .fillMaxWidth()
            .height(34.dp)
            .clip(RoundedCornerShape(17.dp))
            .background(colors.fill)
            .padding(3.dp),
    ) {
        val segmentWidth = maxWidth / options.size
        val thumbOffset by animateDpAsState(
            targetValue = segmentWidth * selectedIndex,
            animationSpec = tween(220),
            label = "segmentThumb",
        )

        // 指示块：真实宽度 = 1/N，位置按 N 等分滑动（此前误用 dp 导致错位）
        Box(
            modifier = Modifier
                .width(segmentWidth)
                .height(28.dp)
                .offset(x = thumbOffset)
                .shadow(if (LocalIsDark.current) 0.dp else 2.dp, RoundedCornerShape(14.dp))
                .clip(RoundedCornerShape(14.dp))
                .background(colors.card),
        )

        Row(Modifier.fillMaxWidth().height(28.dp), verticalAlignment = Alignment.CenterVertically) {
            options.forEachIndexed { index, label ->
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .height(28.dp)
                        .clip(RoundedCornerShape(14.dp))
                        .clickable(
                            interactionSource = remember { MutableInteractionSource() },
                            indication = null,
                        ) { onSelected(index) },
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = label,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        color = if (index == selectedIndex) colors.label else colors.secondaryLabel,
                        textAlign = TextAlign.Center,
                    )
                }
            }
        }
    }
}

// ---------- 胶囊进度条（全站唯一进度样式） ----------

@Composable
fun IosBar(
    progress: Float?,
    color: Color,
    modifier: Modifier = Modifier,
    height: Dp = 8.dp,
) {
    val colors = iosColors()
    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(height)
            .clip(CircleShape)
            .background(colors.fill),
    ) {
        if (progress != null && progress > 0f) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(progress.coerceIn(0f, 1f))
                    .height(height)
                    .clip(CircleShape)
                    .background(color),
            )
        }
    }
}

/** 状态胶囊（正常/已过期/未配置…）：淡色底 + 语义色文字。 */
@Composable
fun IosStatusPill(text: String, tint: Color) {
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(10.dp))
            .background(tint.copy(alpha = 0.14f))
            .padding(horizontal = 9.dp, vertical = 4.dp),
    ) {
        Text(text, fontSize = 13.sp, fontWeight = FontWeight.Medium, color = tint)
    }
}

/** 行内水平排列辅助（标题 + 右侧内容）。 */
@Composable
fun IosLabeledRow(title: String, trailing: @Composable () -> Unit) {
    val colors = iosColors()
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(title, fontSize = 16.sp, color = colors.label)
        trailing()
    }
}
